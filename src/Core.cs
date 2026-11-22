using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace CompLock
{
    static class Log
    {
        static readonly object gate = new object();
        public static void Write(string s)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Web.DataDir);
                    string f = Path.Combine(Web.DataDir, "log.txt");
                    if (File.Exists(f) && new FileInfo(f).Length > 200000) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s + "\r\n");
                }
            }
            catch { }
        }
    }

    static class Web
    {
        public const int AppVersion = 1;
        public static string Base = "https://deadlockconfig.com";
        public static string LastRaw;
        public static readonly string DataDir = Environment.GetEnvironmentVariable("COMPLOCK_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompLock");
        static string CachePath { get { return Path.Combine(DataDir, "pack-data.json"); } }

        public static void Init()
        {
            string s = Environment.GetEnvironmentVariable("COMPLOCK_SITE");
            if (!string.IsNullOrEmpty(s)) Base = s.TrimEnd('/');
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            ServicePointManager.DefaultConnectionLimit = 4;
        }

        public static HttpWebResponse Get(string url, int timeoutMs) { return (HttpWebResponse)Request(url, timeoutMs).GetResponse(); }
        public static HttpWebRequest Request(string url, int timeoutMs)
        {
            HttpWebRequest q = (HttpWebRequest)WebRequest.Create(url);
            q.UserAgent = "CompLock-App/" + AppVersion;
            q.Timeout = timeoutMs;
            q.ReadWriteTimeout = Math.Max(timeoutMs, 30000);
            q.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return q;
        }

        static JObj Checked(string json)
        {
            JObj d = Json.Parse(json) as JObj;
            if (d == null || d.Obj("files") == null || d.Arr("base") == null || d.Arr("option") == null || d.Str("gameinfo") == null)
                throw new Exception("the site's pack data isn't in the form this app knows");
            return d;
        }

        public static JObj Cached()
        {
            try { return File.Exists(CachePath) ? Checked(File.ReadAllText(CachePath, Encoding.UTF8)) : null; }
            catch { return null; }
        }

        public static JObj Load(int timeoutMs, out bool live, out string error)
        {
            live = false; error = null;
            try
            {
                string html;
                using (HttpWebResponse r = Get(Base + "/", timeoutMs))
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                    html = sr.ReadToEnd();
                Match m = Regex.Match(html, "<script id=\"pack-data\" type=\"application/json\">(.*?)</script>", RegexOptions.Singleline);
                if (!m.Success) throw new Exception("the site's main page has no pack data");
                JObj d = Checked(m.Groups[1].Value);
                try { Directory.CreateDirectory(DataDir); File.WriteAllText(CachePath, m.Groups[1].Value, new UTF8Encoding(false)); } catch { }
                LastRaw = m.Groups[1].Value;
                live = true;
                return d;
            }
            catch (Exception e)
            {
                error = e.Message;
                Log.Write("site: " + e.Message);
                return Cached();
            }
        }
    }

    sealed class Settings
    {
        public string Game;
        static string File_ { get { return Path.Combine(Web.DataDir, "settings.json"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(File_)) return s;
                JObj o = Json.Parse(File.ReadAllText(File_, Encoding.UTF8)) as JObj;
                if (o != null) s.Game = o.Str("game");
            }
            catch { }
            return s;
        }
        public void Save()
        {
            try
            {
                JObj o = new JObj();
                o["game"] = Game;
                Directory.CreateDirectory(Web.DataDir);
                File.WriteAllText(File_, Json.Write(o), new UTF8Encoding(false));
            }
            catch { }
        }
        public string Citadel()
        {
            string c = Steam.CitadelOf(Environment.GetEnvironmentVariable("COMPLOCK_GAME")) ?? Steam.CitadelOf(Game);
            return c ?? Steam.FindGame();
        }
    }

    static class Steam
    {
        public const string AppId = "1422450";

        public static string CitadelOf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            foreach (string sub in new string[] { "game\\citadel", "citadel", "" })
            {
                try
                {
                    string d = sub == "" ? dir : Path.Combine(dir, sub);
                    if (File.Exists(Path.Combine(d, "gameinfo.gi")) && (Directory.Exists(Path.Combine(d, "addons")) || File.Exists(Path.Combine(d, "pak01_dir.vpk"))))
                        return Path.GetFullPath(d);
                }
                catch { }
            }
            return null;
        }

        public static List<string> Roots()
        {
            List<string> roots = new List<string>();
            string test = Environment.GetEnvironmentVariable("COMPLOCK_STEAM");
            if (!string.IsNullOrEmpty(test)) { roots.Add(test); return roots; }
            foreach (string[] k in new string[][] {
                new string[] { "HKEY_CURRENT_USER\\Software\\Valve\\Steam", "SteamPath" },
                new string[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Valve\\Steam", "InstallPath" },
                new string[] { "HKEY_LOCAL_MACHINE\\SOFTWARE\\Valve\\Steam", "InstallPath" } })
            {
                try { string v = Registry.GetValue(k[0], k[1], null) as string; if (!string.IsNullOrEmpty(v) && !roots.Contains(v.Replace('/', '\\'))) roots.Add(v.Replace('/', '\\')); } catch { }
            }
            return roots;
        }

        public static string FindGame()
        {
            List<string> libs = new List<string>();
            foreach (string root in Roots())
            {
                try
                {
                    string vdf = File.ReadAllText(Path.Combine(root, "steamapps\\libraryfolders.vdf"));
                    foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\"")) libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch { }
                libs.Add(root);
            }
            foreach (string lib in libs)
            {
                try
                {
                    string acf = Path.Combine(lib, "steamapps\\appmanifest_" + AppId + ".acf");
                    if (!File.Exists(acf)) continue;
                    Match m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                    string c = CitadelOf(Path.Combine(lib, "steamapps\\common\\" + (m.Success ? m.Groups[1].Value : "Deadlock")));
                    if (c != null) return c;
                }
                catch { }
            }
            return null;
        }

        public static bool Running(string citadel)
        {
            string root = Path.GetFullPath(Path.Combine(citadel, "..\\..")).TrimEnd('\\') + "\\";
            foreach (string n in new string[] { "deadlock", "project8", "citadel" })
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(n); } catch { continue; }
                bool here = false;
                foreach (Process p in ps)
                {
                    try { if (Path.GetFullPath(p.MainModule.FileName).StartsWith(root, StringComparison.OrdinalIgnoreCase)) here = true; }
                    catch { here = true; }
                    p.Dispose();
                }
                if (here) return true;
            }
            return false;
        }
    }

    static class Crc32
    {
        static readonly uint[] T = Table();
        static uint[] Table()
        {
            uint[] t = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
            return t;
        }
        public static uint Update(uint crc, byte[] b, int n) { for (int i = 0; i < n; i++) crc = T[(crc ^ b[i]) & 255] ^ (crc >> 8); return crc; }
        public static uint Of(byte[] b) { return Update(0xFFFFFFFFu, b, b.Length) ^ 0xFFFFFFFFu; }
        public static uint OfFile(string path)
        {
            uint crc = 0xFFFFFFFFu;
            byte[] buf = new byte[1 << 17];
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 17, FileOptions.SequentialScan))
            {
                int r;
                while ((r = f.Read(buf, 0, buf.Length)) > 0) crc = Update(crc, buf, r);
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }

    sealed class State
    {
        public bool Exists;
        public string Pack = "";
        public JObj Files = new JObj();
        public long GiSize = -1;
        public uint GiCrc;
        public bool? Video;
        public int? Scale;

        public static State Load(string addons)
        {
            State s = new State();
            try
            {
                string f = Path.Combine(addons, "complock.json");
                if (!File.Exists(f)) return s;
                JObj o = Json.Parse(File.ReadAllText(f, Encoding.UTF8)) as JObj;
                if (o == null) return s;
                s.Exists = true;
                s.Pack = o.Str("pack") ?? "";
                s.Files = o.Obj("files") ?? new JObj();
                JObj g = o.Obj("gameinfo");
                if (g != null) { s.GiSize = g.Long("size"); s.GiCrc = (uint)g.Long("crc"); }
                if (o.Has("video")) s.Video = o.Bool("video");
                if (o.Has("render_scale")) s.Scale = (int)o.Long("render_scale");
            }
            catch (Exception e) { Log.Write("state: " + e.Message); }
            return s;
        }

        public void Save(string addons)
        {
            JObj o = new JObj();
            o["app"] = (double)Web.AppVersion;
            o["pack"] = Pack;
            o["files"] = Files;
            JObj g = new JObj();
            g["size"] = (double)GiSize; g["crc"] = (double)GiCrc;
            o["gameinfo"] = g;
            if (Video != null) o["video"] = (bool)Video;
            if (Scale != null) o["render_scale"] = (double)(int)Scale;
            File.WriteAllText(Path.Combine(addons, "complock.json"), Json.Write(o), new UTF8Encoding(false));
            Exists = true;
        }

        public static JObj Entry(string src, FileInfo fi, uint crc, bool keep)
        {
            JObj e = new JObj();
            e["src"] = src; e["size"] = (double)fi.Length; e["crc"] = (double)crc; e["mtime"] = (double)fi.LastWriteTimeUtc.Ticks;
            if (keep) e["keep"] = true;
            return e;
        }
    }

    enum Gi { Ok, Outdated, Reset, Edited }

    sealed class Check
    {
        public string Citadel, Addons, ExpectedGameinfo, ExpectedMods;
        public string InstalledCode, WasGameinfo;
        public Resolved R;
        public State St;
        public List<PakPart> Need = new List<PakPart>();
        public List<PakPart> Updates = new List<PakPart>();
        public Dictionary<PakPart, string> Reuse = new Dictionary<PakPart, string>();
        public Dictionary<PakPart, PakFile> Owner = new Dictionary<PakPart, PakFile>();
        public HashSet<string> Current = new HashSet<string>();
        public List<string> Changed = new List<string>();
        public List<string> Foreign = new List<string>();
        public List<string> Stale = new List<string>();
        public List<string> Kept = new List<string>();
        public List<string> LookAlike = new List<string>();
        public List<string> OldCopies = new List<string>();
        public Dictionary<string, string> OldOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Who = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string PerfPreset;
        public List<string> VideoFiles = new List<string>();
        public string VideoSeed;
        public bool VideoOn, VideoOk, VideoDiffers, VideoPending, VideoUpdate;
        public Gi Gameinfo;
        public bool GameinfoUpdate, GameinfoPending;
        public bool Installed, Saved;

        public List<PakPart> Installs { get { List<PakPart> l = new List<PakPart>(); foreach (PakPart p in Need) if (!Updates.Contains(p)) l.Add(p); return l; } }
        public bool HasUpdates { get { return Updates.Count > 0 || GameinfoUpdate || VideoUpdate; } }
        public bool Pending { get { return Installs.Count > 0 || Stale.Count > 0 || (Gameinfo != Gi.Ok && GameinfoPending) || !Saved || VideoPending || OldCopies.Count > 0; } }
        public bool UpToDate { get { return !Pending && !HasUpdates; } }
        public long DownOf(IEnumerable<PakPart> parts) { long n = 0; foreach (PakPart p in parts) if (!Reuse.ContainsKey(p)) n += p.CSize; return n; }
        public List<string> Titles(IEnumerable<PakPart> parts)
        {
            List<string> s = new List<string>();
            foreach (PakPart p in parts) if (!s.Contains(Owner[p].Title)) s.Add(Owner[p].Title);
            return s;
        }
    }

    sealed class Plan
    {
        public HashSet<string> Only;
        public List<string> Keep = new List<string>();
        public HashSet<string> Replace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool Stale, Save, Offline, Cleanup;
        public List<string> Drop = new List<string>();
        public string Gameinfo;
        public bool Video;

        public static Plan Full(Check c)
        {
            Plan p = new Plan();
            p.Stale = true; p.Save = true; p.Cleanup = true;
            p.Gameinfo = c.Gameinfo != Gi.Ok ? c.ExpectedGameinfo : null;
            p.Video = c.VideoOn && c.VideoDiffers;
            return p;
        }
        public static Plan Apply(Check c)
        {
            Plan p = new Plan();
            p.Only = new HashSet<string>();
            foreach (PakPart q in c.Installs) p.Only.Add(q.Name);
            p.Stale = true; p.Save = true; p.Cleanup = true;
            p.Gameinfo = c.Gameinfo != Gi.Ok && c.GameinfoPending ? c.ExpectedGameinfo : null;
            p.Video = c.VideoOn && c.VideoPending;
            return p;
        }
        public static Plan Update(Check c, string id, bool gameinfo)
        {
            Plan p = new Plan();
            p.Only = new HashSet<string>();
            List<string> files = id != null && c.R.ByItem.ContainsKey(id) ? c.R.ByItem[id] : new List<string>();
            foreach (PakPart q in c.Updates) if (id == null || files.Contains(c.Owner[q].File)) p.Only.Add(q.Name);
            p.Gameinfo = (id == null || gameinfo) && c.GameinfoUpdate ? c.WasGameinfo : null;
            p.Video = (id == null || id == "video") && c.VideoUpdate;
            return p;
        }
    }

    static class Installer
    {
        static readonly Regex PakName = new Regex("^pak(\\d\\d)_(dir|\\d{3})\\.vpk$", RegexOptions.IgnoreCase);
        static readonly Dictionary<string, uint> crcCache = new Dictionary<string, uint>();

        static uint CrcOf(string path, State st)
        {
            FileInfo fi = new FileInfo(path);
            string key = path.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            uint c;
            lock (crcCache) if (crcCache.TryGetValue(key, out c)) return c;
            JObj e = st != null ? st.Files.Obj(fi.Name) : null;
            if (e != null && e.Long("size") == fi.Length && e.Long("mtime") == (long)(double)fi.LastWriteTimeUtc.Ticks) c = (uint)e.Long("crc");
            else c = Crc32.OfFile(path);
            lock (crcCache) crcCache[key] = c;
            return c;
        }

        static string ReadText(string path) { try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; } catch { return null; } }
        static bool HasAddonsPath(string gameinfo) { return gameinfo != null && Regex.IsMatch(gameinfo, "^\\s*Game\\s+\"?citadel/addons", RegexOptions.Multiline); }

        public static Dictionary<string, string> Recognize(Pack pack, string addons, State st)
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(addons)) return found;
            Dictionary<long, List<KeyValuePair<string, PakPart>>> bySize = new Dictionary<long, List<KeyValuePair<string, PakPart>>>();
            foreach (string name in pack.Files.Keys)
                foreach (PakPart p in pack.FileOf(name).Parts)
                {
                    if (!bySize.ContainsKey(p.Size)) bySize[p.Size] = new List<KeyValuePair<string, PakPart>>();
                    bySize[p.Size].Add(new KeyValuePair<string, PakPart>(name, p));
                }
            foreach (string f in Directory.GetFiles(addons, "pak*.vpk"))
            {
                if (!PakName.IsMatch(Path.GetFileName(f))) continue;
                List<KeyValuePair<string, PakPart>> same;
                if (!bySize.TryGetValue(new FileInfo(f).Length, out same)) continue;
                uint crc;
                try { crc = CrcOf(f, st); } catch { continue; }
                foreach (KeyValuePair<string, PakPart> kv in same) if (kv.Value.Crc == crc) { found[f] = kv.Key; break; }
            }
            return found;
        }

        public static string InstalledCode(string citadel)
        {
            string addons = Path.Combine(citadel, "addons");
            State st = State.Load(addons);
            return st.Exists ? st.Pack : Pack.LinkOf(ReadText(Path.Combine(addons, "mods.txt")));
        }

        public static Dictionary<string, object> Installed(Pack pack, string citadel)
        {
            string addons = Path.Combine(citadel, "addons");
            string code = InstalledCode(citadel);
            if (code != null) return pack.Decode(code);
            Dictionary<string, string> exact = Recognize(pack, addons, null);
            HashSet<string> have = new HashSet<string>(exact.Values);
            HashSet<string> twins = Twins(pack, addons, exact);
            if (have.Count == 0 && twins.Count == 0) return null;
            string gi = ReadText(Path.Combine(citadel, "gameinfo.gi")) ?? "";
            List<string> parts = new List<string>();
            foreach (JObj b in pack.BaseChoice)
            {
                JObj variants = b.Obj("variants");
                foreach (JObj c in Pack.Choices(b))
                {
                    string file = c.Str("flag") != null && variants != null ? variants.Str(c.Str("flag")) : b.Str("pak");
                    if (file != null && have.Contains(file)) parts.Add(b.Str("id") + ":" + c.Str("id"));
                }
            }
            foreach (JObj o in pack.Options)
            {
                string id = o.Str("id");
                List<JObj> choices = Pack.Choices(o);
                bool on = false;
                if (choices.Count > 0)
                {
                    foreach (JObj c in choices) if (c.Str("pak") != null && have.Contains(c.Str("pak"))) { parts.Add(id + ":" + c.Str("id")); on = true; }
                    if (!on) foreach (JObj c in choices) if (c.Str("pak") != null && twins.Contains(c.Str("pak"))) { parts.Add(id + ":" + Pack.DefaultChoice(o).Str("id")); on = true; break; }
                }
                else if (o.Str("pak") != null) { on = have.Contains(o.Str("pak")) || twins.Contains(o.Str("pak")); if (on) parts.Add(id); }
                else if (o.Obj("gameinfo") != null)
                {
                    on = true;
                    foreach (string k in o.Obj("gameinfo").Keys)
                        if (!Regex.IsMatch(gi, "^\\s*\"" + Regex.Escape(k) + "\"\\s+\"" + Regex.Escape((string)o.Obj("gameinfo")[k]) + "\"", RegexOptions.Multiline)) on = false;
                    if (on) parts.Add(id);
                }
                if (!on && o.Has("gameinfo_else")) parts.Add(id + ":off");
            }
            if (pack.Fov != null)
            {
                Match m = Regex.Match(gi, "^\\s*\"" + Regex.Escape(pack.FovCvar) + "\"\\s+\"([^\"\\r\\n]*)\"", RegexOptions.Multiline);
                if (m.Success) parts.Add("fov:" + m.Groups[1].Value);
            }
            return pack.Decode(string.Join(",", parts.ToArray()));
        }

        static HashSet<string> Twins(Pack pack, string addons, Dictionary<string, string> exact)
        {
            HashSet<string> found = new HashSet<string>();
            if (!Directory.Exists(addons)) return found;
            foreach (string f in Directory.GetFiles(addons, "pak*.vpk"))
            {
                string name = Path.GetFileName(f);
                if (!PakName.IsMatch(name) || !name.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) || exact.ContainsKey(f)) continue;
                foreach (string site in pack.Files.Keys)
                    foreach (PakPart p in pack.FileOf(site).Parts)
                        if (Vpk.Twin(f, p)) found.Add(site);
            }
            return found;
        }

        public static Check Look(Pack pack, string citadel)
        {
            Check c = new Check();
            c.Citadel = citadel;
            c.Addons = Path.Combine(citadel, "addons");
            c.R = pack.Resolve();
            c.St = State.Load(c.Addons);
            string cur = ReadText(Path.Combine(citadel, "gameinfo.gi"));
            c.ExpectedGameinfo = Pack.Gameinfo(pack.GameinfoText, c.R.Lines, c.R.Off);
            c.PerfPreset = GameinfoPaths.PerfPreset(cur);
            c.ExpectedMods = pack.ModsTxt(c.R);
            Managers managers = new Managers(citadel);
            c.InstalledCode = InstalledCode(citadel);
            string mods = ReadText(Path.Combine(c.Addons, "mods.txt"));
            Dictionary<string, string> known = Recognize(pack, c.Addons, c.St);
            HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in c.St.Files.Keys) owned.Add(n);
            foreach (string n in Pack.Listed(mods)) owned.Add(n);
            foreach (string f in new List<string>(known.Keys)) if (!owned.Contains(Path.GetFileName(f)) && managers.DmmHas(Path.GetFileName(f))) known.Remove(f);
            foreach (string f in known.Keys) owned.Add(Path.GetFileName(f));
            c.Installed = c.St.Exists || owned.Count > 0;

            HashSet<string> wasFiles = new HashSet<string>();
            if (c.InstalledCode != null)
            {
                Pack was = new Pack(pack.D);
                was.Sel = was.Decode(c.InstalledCode);
                Resolved wr = was.Resolve();
                foreach (PakFile pf in wr.Paks) wasFiles.Add(pf.File);
                c.WasGameinfo = Pack.Gameinfo(pack.GameinfoText, wr.Lines, wr.Off);
            }

            HashSet<string> wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PakFile pf in c.R.Paks)
            {
                bool current = true;
                foreach (PakPart p in pf.Parts)
                {
                    c.Owner[p] = pf;
                    wanted.Add(p.Name);
                    string path = Path.Combine(c.Addons, p.Name);
                    bool foreign = false;
                    if (File.Exists(path))
                    {
                        FileInfo fi = new FileInfo(path);
                        uint crc = CrcOf(path, c.St);
                        if (fi.Length == p.Size && crc == p.Crc) continue;
                        JObj e = c.St.Files.Obj(p.Name);
                        bool asWritten = e != null && e.Long("size") == fi.Length && (uint)e.Long("crc") == crc;
                        if (e != null && e.Bool("keep") && asWritten) { c.Kept.Add(p.Name); current = false; continue; }
                        if (!asWritten && !known.ContainsKey(path))
                        {
                            bool alike = Vpk.Alike(path, p), ours = owned.Contains(p.Name);
                            if (ours && alike) { if (e != null) c.Changed.Add(p.Name); }
                            else
                            {
                                c.Foreign.Add(p.Name); foreign = true;
                                if (alike) c.LookAlike.Add(p.Name);
                                string who = managers.Who(p.Name);
                                if (who != null) c.Who[p.Name] = who;
                            }
                        }
                    }
                    current = false;
                    c.Need.Add(p);
                    if (wasFiles.Contains(pf.File) && !foreign) c.Updates.Add(p);
                    foreach (KeyValuePair<string, string> kv in known)
                    {
                        if (string.Equals(Path.GetFileName(kv.Key), p.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (new FileInfo(kv.Key).Length == p.Size && CrcOf(kv.Key, c.St) == p.Crc) { c.Reuse[p] = kv.Key; break; }
                    }
                }
                if (current) c.Current.Add(pf.File);
            }

            foreach (string n in owned)
            {
                if (wanted.Contains(n)) continue;
                string path = Path.Combine(c.Addons, n);
                if (!File.Exists(path)) continue;
                JObj e = c.St.Files.Obj(n);
                bool mine = known.ContainsKey(path) || e == null;
                if (!mine)
                {
                    FileInfo fi = new FileInfo(path);
                    mine = !e.Bool("keep") && e.Long("size") == fi.Length && (uint)e.Long("crc") == CrcOf(path, c.St);
                }
                if (mine) c.Stale.Add(n);
            }

            foreach (string f in Directory.Exists(c.Addons) ? Directory.GetFiles(c.Addons, "pak*.vpk") : new string[0])
            {
                string name = Path.GetFileName(f);
                if (!PakName.IsMatch(name) || !name.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase)) continue;
                if (owned.Contains(name) || wanted.Contains(name) || managers.DmmHas(name)) continue;
                foreach (PakFile pf in c.R.Paks)
                {
                    bool twin = false;
                    foreach (PakPart p in pf.Parts) if (Vpk.Twin(f, p)) twin = true;
                    if (!twin) continue;
                    c.OldCopies.Add(name);
                    c.OldOf[name] = pf.Title;
                    string who = managers.Who(name);
                    if (who != null) c.Who[name] = who;
                    break;
                }
            }

            if (GameinfoPaths.Same(cur, c.ExpectedGameinfo)) c.Gameinfo = Gi.Ok;
            else
            {
                byte[] raw = Encoding.UTF8.GetBytes(cur ?? "");
                if (c.St.Exists && c.St.GiSize == raw.Length && c.St.GiCrc == Crc32.Of(raw)) c.Gameinfo = Gi.Outdated;
                else c.Gameinfo = HasAddonsPath(cur) ? Gi.Edited : Gi.Reset;
            }
            c.GameinfoPending = c.WasGameinfo == null || c.WasGameinfo != c.ExpectedGameinfo;
            c.GameinfoUpdate = c.WasGameinfo != null && c.Gameinfo != Gi.Ok && !GameinfoPaths.Same(cur, c.WasGameinfo);
            c.Saved = c.St.Exists && c.St.Pack == pack.Encode() && mods == c.ExpectedMods;

            c.VideoOn = pack.LowVideo;
            string seed;
            c.VideoFiles = Video.Targets(citadel, c.ExpectedGameinfo, out seed);
            c.VideoSeed = seed;
            bool any = false;
            foreach (string f in c.VideoFiles)
            {
                string text = ReadText(f);
                if (text != null) { any = true; if (!Video.IsMin(text, pack.RenderScale)) c.VideoDiffers = true; }
                else if (seed != null) { any = true; c.VideoDiffers = true; }
            }
            bool had = c.St.Video == true, sameScale = c.St.Scale == pack.RenderScale;
            c.VideoOk = c.VideoOn && any && !c.VideoDiffers;
            c.VideoPending = c.VideoOn ? c.VideoDiffers && (!had || !sameScale) : had;
            c.VideoUpdate = c.VideoOn && had && sameScale && c.VideoDiffers;
            return c;
        }

        static void Fetch(PakPart p, string dest, Action<int> tick)
        {
            using (PartStream src = new PartStream(p.Urls, tick))
            using (DeflateStream z = new DeflateStream(src, CompressionMode.Decompress))
            using (FileStream f = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buf = new byte[1 << 16];
                uint crc = 0xFFFFFFFFu;
                long n = 0;
                int r;
                while ((r = z.Read(buf, 0, buf.Length)) > 0)
                {
                    n += r;
                    if (n > p.Size) throw new Exception(p.Name + " is larger than the site says; nothing replaced");
                    f.Write(buf, 0, r);
                    crc = Crc32.Update(crc, buf, r);
                    sha.TransformBlock(buf, 0, r, null, 0);
                }
                while (src.Read(buf, 0, buf.Length) > 0) { }
                sha.TransformFinalBlock(buf, 0, 0);
                string hex = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                Match m = Regex.Match(p.Urls[0], "\\.([0-9a-f]{10})(\\.\\d+)?\\.z$");
                if (n != p.Size || (crc ^ 0xFFFFFFFFu) != p.Crc || src.Total != p.CSize || (m.Success && !hex.StartsWith(m.Groups[1].Value)))
                    throw new Exception(p.Name + " didn't check out; nothing replaced");
            }
        }

        static int FreeNumber(Pack pack, string addons, HashSet<int> taken)
        {
            int top = 0;
            foreach (string name in pack.Files.Keys) top = Math.Max(top, pack.FileOf(name).Num);
            HashSet<int> used = new HashSet<int>(taken);
            foreach (string f in Directory.GetFiles(addons, "pak*.vpk"))
            {
                Match m = PakName.Match(Path.GetFileName(f));
                if (m.Success) used.Add(int.Parse(m.Groups[1].Value));
            }
            string parked = Path.Combine(addons, ".disabled");
            if (Directory.Exists(parked))
                foreach (string f in Directory.GetFiles(parked, "pak*.vpk"))
                {
                    Match m = PakName.Match(Path.GetFileName(f));
                    if (m.Success) used.Add(int.Parse(m.Groups[1].Value));
                }
            for (int n = top + 1; n < 100; n++) if (!used.Contains(n)) return n;
            throw new Exception("no free pak number left in addons");
        }

        public sealed class Result
        {
            public List<string> Updated = new List<string>(), Removed = new List<string>(), Moved = new List<string>(), Left = new List<string>();
            public bool Gameinfo, BackedUp, Video;
            public int Named;
        }

        public static Result Install(Pack pack, Check c, Plan plan, Action<long, long> progress)
        {
            bool mine;
            using (Mutex mx = new Mutex(true, "Local\\CompLockInstall", out mine))
            {
                if (!mine) throw new Exception("CompLock is already installing in another window");
                try { return Run(pack, c, plan, progress ?? delegate { }); }
                finally { mx.ReleaseMutex(); }
            }
        }

        static Result Run(Pack pack, Check c, Plan plan, Action<long, long> progress)
        {
            if (Steam.Running(c.Citadel)) throw new Exception("Deadlock is running. Close it first");
            Result res = new Result();
            Directory.CreateDirectory(c.Addons);
            string tmp = Path.Combine(c.Addons, ".complock-tmp");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            try
            {
                List<PakPart> todo = new List<PakPart>();
                foreach (PakPart p in c.Need)
                {
                    if (plan.Only != null && !plan.Only.Contains(p.Name)) continue;
                    if (plan.Keep.Contains(p.Name) || (plan.Offline && !c.Reuse.ContainsKey(p))) { res.Left.Add(p.Name); continue; }
                    todo.Add(p);
                }
                long total = 0, got = 0;
                foreach (PakPart p in todo) if (!c.Reuse.ContainsKey(p)) total += p.CSize;
                progress(0, total);
                foreach (PakPart p in todo)
                    if (!c.Reuse.ContainsKey(p))
                        Fetch(p, Path.Combine(tmp, p.Name), delegate (int n) { got += n; progress(got, total); });
                HashSet<int> taken = new HashSet<int>();
                Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (PakPart p in todo)
                {
                    if (!c.Foreign.Contains(p.Name)) continue;
                    string nn = PakName.Match(p.Name).Groups[1].Value;
                    if (plan.Replace.Contains(p.Name))
                    {
                        foreach (string f in Directory.GetFiles(c.Addons, "pak" + nn + "_*.vpk"))
                            if (Regex.IsMatch(Path.GetFileName(f), "^pak\\d\\d_\\d{3}\\.vpk$", RegexOptions.IgnoreCase)) File.Delete(f);
                        continue;
                    }
                    int free = FreeNumber(pack, c.Addons, taken);
                    taken.Add(free);
                    foreach (string f in Directory.GetFiles(c.Addons, "pak" + nn + "_*.vpk"))
                    {
                        Match m = PakName.Match(Path.GetFileName(f));
                        if (!m.Success) continue;
                        string to = "pak" + free.ToString("00") + "_" + m.Groups[2].Value + ".vpk";
                        File.Move(f, Path.Combine(c.Addons, to));
                        renamed[Path.GetFileName(f)] = to;
                        res.Moved.Add(Path.GetFileName(f) + " to " + to);
                    }
                }
                foreach (PakPart p in todo)
                    if (c.Reuse.ContainsKey(p) && File.Exists(c.Reuse[p])) File.Move(c.Reuse[p], Path.Combine(tmp, p.Name));
                    else if (c.Reuse.ContainsKey(p)) Fetch(p, Path.Combine(tmp, p.Name), delegate { });
                foreach (PakPart p in todo)
                {
                    string to = Path.Combine(c.Addons, p.Name);
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(Path.Combine(tmp, p.Name), to);
                    c.St.Files[p.Name] = State.Entry(c.Owner[p].File, new FileInfo(to), p.Crc, false);
                    if (!res.Updated.Contains(c.Owner[p].Title)) res.Updated.Add(c.Owner[p].Title);
                }
                foreach (string n in plan.Keep)
                {
                    string path = Path.Combine(c.Addons, n);
                    if (File.Exists(path)) c.St.Files[n] = State.Entry("", new FileInfo(path), Crc32.OfFile(path), true);
                }
                if (plan.Stale)
                    foreach (string n in c.Stale)
                    {
                        string path = Path.Combine(c.Addons, n);
                        if (File.Exists(path)) { File.Delete(path); res.Removed.Add(n); }
                        c.St.Files.Remove(n);
                    }
                foreach (string n in plan.Drop)
                {
                    Match m = PakName.Match(n);
                    if (!m.Success) continue;
                    foreach (string f in Directory.GetFiles(c.Addons, "pak" + m.Groups[1].Value + "_*.vpk"))
                        if (PakName.IsMatch(Path.GetFileName(f))) File.Delete(f);
                    res.Removed.Add(n);
                    c.St.Files.Remove(n);
                }
                foreach (string n in new List<string>(c.St.Files.Keys))
                    if (!File.Exists(Path.Combine(c.Addons, n))) c.St.Files.Remove(n);
                string gi = Path.Combine(c.Citadel, "gameinfo.gi");
                if (plan.Gameinfo != null)
                {
                    byte[] want = new UTF8Encoding(false).GetBytes(GameinfoPaths.Keep(plan.Gameinfo, ReadText(gi)));
                    if (File.Exists(gi) && (c.Gameinfo == Gi.Reset || (c.Gameinfo == Gi.Edited && !File.Exists(gi + ".complock-bak"))))
                    {
                        File.Copy(gi, gi + ".complock-bak", true);
                        res.BackedUp = true;
                    }
                    File.WriteAllBytes(gi, want);
                    c.St.GiSize = want.Length; c.St.GiCrc = Crc32.Of(want);
                    res.Gameinfo = true;
                }
                else if (c.Gameinfo == Gi.Ok)
                {
                    byte[] have = new UTF8Encoding(false).GetBytes(c.ExpectedGameinfo);
                    c.St.GiSize = have.Length; c.St.GiCrc = Crc32.Of(have);
                }
                if (plan.Video)
                {
                    foreach (string f in c.VideoFiles) if (Video.Apply(f, c.VideoSeed, pack.RenderScale)) res.Video = true;
                    c.St.Video = true; c.St.Scale = pack.RenderScale;
                }
                bool complete = true;
                foreach (string n in res.Left) if (!plan.Keep.Contains(n)) complete = false;
                if (plan.Save && complete)
                {
                    File.WriteAllText(Path.Combine(c.Addons, "mods.txt"), c.ExpectedMods, new UTF8Encoding(false));
                    c.St.Pack = pack.Encode();
                    if (!c.VideoOn) c.St.Video = false; else if (c.VideoOk) { c.St.Video = true; c.St.Scale = pack.RenderScale; }
                }
                else if (!c.St.Exists) c.St.Pack = c.InstalledCode ?? "";
                c.St.Save(c.Addons);
                try
                {
                    List<string> ours = new List<string>();
                    foreach (PakFile pf in c.R.Paks)
                        foreach (PakPart p in pf.Parts)
                        {
                            string at = Path.Combine(c.Addons, p.Name);
                            if (!File.Exists(at)) continue;
                            JObj e = c.St.Files.Obj(p.Name);
                            if (e != null ? !e.Bool("keep") : new FileInfo(at).Length == p.Size && CrcOf(at, c.St) == p.Crc) ours.Add(p.Name);
                        }
                    res.Named = Managers.NameInGrimoire(c.Citadel, renamed, ours);
                }
                catch (Exception e) { Log.Write("grimoire: " + e.Message); }
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }
            Log.Write("installed: " + (res.Updated.Count > 0 ? string.Join(", ", res.Updated.ToArray()) : "no pak") + (res.Gameinfo ? ", gameinfo.gi" : "") + (res.Video ? ", video.txt at the lowest settings" : "")
                + (res.Removed.Count > 0 ? "; removed " + string.Join(", ", res.Removed.ToArray()) : "") + (res.Moved.Count > 0 ? "; moved " + string.Join(", ", res.Moved.ToArray()) : "")
                + (res.Left.Count > 0 ? "; left alone " + string.Join(", ", res.Left.ToArray()) : ""));
            return res;
        }

        public static bool Remove(Pack pack, string citadel, out bool gameinfoRestored)
        {
            if (Steam.Running(citadel)) throw new Exception("Deadlock is running. Close it first");
            string addons = Path.Combine(citadel, "addons");
            State st = State.Load(addons);
            HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in st.Files.Keys) if (!st.Files.Obj(n).Bool("keep")) owned.Add(n);
            foreach (string n in Pack.Listed(ReadText(Path.Combine(addons, "mods.txt")))) if (st.Files.Obj(n) == null || !st.Files.Obj(n).Bool("keep")) owned.Add(n);
            foreach (string f in Recognize(pack, addons, st).Keys) owned.Add(Path.GetFileName(f));
            bool any = false;
            foreach (string n in owned)
            {
                string path = Path.Combine(addons, n);
                if (File.Exists(path)) { File.Delete(path); any = true; }
            }
            foreach (string n in new string[] { "mods.txt", "complock.json" })
                if (File.Exists(Path.Combine(addons, n)) && (n != "mods.txt" || (ReadText(Path.Combine(addons, n)) ?? "").StartsWith("CompLock"))) File.Delete(Path.Combine(addons, n));
            string gi = Path.Combine(citadel, "gameinfo.gi"), bak = gi + ".complock-bak";
            gameinfoRestored = File.Exists(bak);
            if (gameinfoRestored) { File.Copy(bak, gi, true); File.Delete(bak); }
            bool video = false;
            try { video = Video.Restore(citadel); } catch (Exception e) { Log.Write("video: " + e.Message); }
            Log.Write("removed" + (gameinfoRestored ? ", gameinfo.gi restored" : "") + (video ? ", video.txt restored" : ""));
            return any;
        }
    }

    sealed class PartStream : Stream
    {
        readonly List<string> urls;
        readonly Action<int> tick;
        int next;
        HttpWebRequest req;
        HttpWebResponse resp;
        Stream cur;
        public long Total;

        public PartStream(List<string> urls, Action<int> tick) { this.urls = urls; this.tick = tick; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (true)
            {
                if (cur == null)
                {
                    if (next >= urls.Count) return 0;
                    req = Web.Request(Web.Base + urls[next++], 30000);
                    resp = (HttpWebResponse)req.GetResponse();
                    cur = resp.GetResponseStream();
                }
                int r = cur.Read(buffer, offset, count);
                if (r > 0) { Total += r; tick(r); return r; }
                cur.Dispose(); resp.Close(); cur = null; resp = null;
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (cur != null && req != null) { try { req.Abort(); } catch { } } if (cur != null) cur.Dispose(); if (resp != null) resp.Close(); }
            base.Dispose(disposing);
        }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }
}
