using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CompLock
{

    static class GameinfoPaths
    {
        sealed class Entry { public string Key, Path, Text, Indent; public int Start; }

        static readonly string[] Known = { "game_uilanguage citadel_*language*", "game_lowviolence citadel_lv", "game citadel", "game core",
            "mod citadel", "write citadel", "mod core", "write core", "game citadel/addons" };

        static bool Find(string text, out int start, out int end)
        {
            start = end = 0;
            Match m = Regex.Match(text, "^[ \\t]*\"?SearchPaths\"?[ \\t]*(?:\\r?\\n[ \\t]*)?\\{[ \\t]*\\r?\\n", RegexOptions.Multiline);
            if (!m.Success) return false;
            start = m.Index + m.Length;
            Match close = new Regex("^[ \\t]*\\}", RegexOptions.Multiline).Match(text, start);
            if (!close.Success) return false;
            end = close.Index;
            return true;
        }

        static List<Entry> Entries(string body)
        {
            List<Entry> list = new List<Entry>();
            int pos = 0;
            while (pos < body.Length)
            {
                int nl = body.IndexOf('\n', pos), stop = nl < 0 ? body.Length : nl + 1;
                string line = body.Substring(pos, stop - pos), code = line;
                int cm = code.IndexOf("//", StringComparison.Ordinal);
                if (cm >= 0) code = code.Substring(0, cm);
                string[] tok = code.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length >= 2)
                {
                    Entry e = new Entry();
                    string k = tok[0].Trim('"'), p = tok[1].Trim('"');
                    e.Key = k.ToLowerInvariant(); e.Path = p.Replace('\\', '/').ToLowerInvariant();
                    e.Text = k + "\t\t\t\t" + p; e.Start = pos;
                    e.Indent = line.Substring(0, line.Length - line.TrimStart(' ', '\t').Length);
                    list.Add(e);
                }
                pos = stop;
            }
            return list;
        }

        public static bool Same(string current, string expected)
        {
            if (current == expected) return true;
            int es, ee, cs, ce;
            if (current == null || expected == null || !Find(expected, out es, out ee) || !Find(current, out cs, out ce)) return false;
            if (current.Substring(0, cs) != expected.Substring(0, es) || current.Substring(ce) != expected.Substring(ee)) return false;
            foreach (Entry e in Entries(current.Substring(cs, ce - cs))) if (e.Key == "game" && e.Path == "citadel/addons") return true;
            return false;
        }

        public static string Keep(string expected, string current)
        {
            int es, ee, cs, ce;
            if (current == null || !Find(expected, out es, out ee) || !Find(current, out cs, out ce)) return expected;
            string body = current.Substring(cs, ce - cs);
            List<Entry> have = Entries(body), want = Entries(expected.Substring(es, ee - es));
            bool theirs = false;
            foreach (Entry h in have) if (Array.IndexOf(Known, h.Key + " " + h.Path) < 0) theirs = true;
            if (!theirs) return expected;
            List<Entry> missing = new List<Entry>();
            foreach (Entry w in want)
            {
                bool there = false;
                foreach (Entry h in have) if (h.Key == w.Key && h.Path == w.Path) there = true;
                if (!there) missing.Add(w);
            }
            if (missing.Count > 0)
            {
                int at = body.Length;
                string indent = have[0].Indent, eol = body.Contains("\r\n") ? "\r\n" : "\n";
                foreach (Entry h in have) if (h.Key == "game" && h.Path == "citadel") { at = h.Start; indent = h.Indent; break; }
                StringBuilder add = new StringBuilder();
                if (at == body.Length && body.Length > 0 && !body.EndsWith("\n")) add.Append(eol);
                add.Append(indent + "// CompLock BEGIN" + eol);
                foreach (Entry m in missing) add.Append(indent + m.Text + eol);
                add.Append(indent + "// CompLock END" + eol);
                body = body.Insert(at, add.ToString());
            }
            return expected.Substring(0, es) + body + expected.Substring(ee);
        }

        public static string PerfPreset(string current)
        {
            if (current == null) return null;
            if (current.IndexOf("// grimoire-perf", StringComparison.OrdinalIgnoreCase) >= 0) return "Grimoire";
            if (current.IndexOf("// dmm-perf", StringComparison.OrdinalIgnoreCase) >= 0) return "Deadlock Mod Manager";
            return null;
        }
    }
    static class Vpk
    {
        static readonly Dictionary<string, HashSet<string>> cache = new Dictionary<string, HashSet<string>>();

        static byte[] Read(Stream s, int n)
        {
            byte[] b = new byte[n];
            int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new EndOfStreamException(); got += r; }
            return b;
        }
        static string Str(byte[] t, ref int i)
        {
            int j = i;
            while (t[j] != 0) j++;
            string s = Encoding.UTF8.GetString(t, i, j - i);
            i = j + 1;
            return s;
        }
        static HashSet<string> Tree(Stream s)
        {
            byte[] h = Read(s, 12);
            uint version = BitConverter.ToUInt32(h, 4), size = BitConverter.ToUInt32(h, 8);
            if (BitConverter.ToUInt32(h, 0) != 0x55aa1234u || (version != 1 && version != 2) || size > (64u << 20)) throw new InvalidDataException("not a pak");
            if (version == 2) Read(s, 16);
            byte[] t = Read(s, (int)size);
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (true)
            {
                string ext = Str(t, ref i);
                if (ext == "") break;
                while (true)
                {
                    string dir = Str(t, ref i);
                    if (dir == "") break;
                    while (true)
                    {
                        string name = Str(t, ref i);
                        if (name == "") break;
                        i += 18 + BitConverter.ToUInt16(t, i + 4);
                        string full = (dir == " " ? "" : dir + "/") + name + (ext == " " ? "" : "." + ext);
                        if (dir != " " || !Regex.IsMatch(full, "^(addoninfo\\.txt|modinfo\\.json|readme(\\.txt|\\.md)?)$", RegexOptions.IgnoreCase)) set.Add(full);
                    }
                }
            }
            return set;
        }

        static HashSet<string> Local(string path)
        {
            FileInfo fi = new FileInfo(path);
            string key = path.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            HashSet<string> t;
            lock (cache) if (cache.TryGetValue(key, out t)) return t;
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) t = Tree(f);
            lock (cache) cache[key] = t;
            return t;
        }
        static HashSet<string> Remote(PakPart p)
        {
            HashSet<string> t;
            lock (cache) if (cache.TryGetValue(p.Urls[0], out t)) return t;
            using (PartStream src = new PartStream(p.Urls, delegate { }))
            using (DeflateStream z = new DeflateStream(src, CompressionMode.Decompress))
                t = Tree(z);
            lock (cache) cache[p.Urls[0]] = t;
            return t;
        }

        public static bool Alike(string path, PakPart p)
        {
            try
            {
                HashSet<string> a = Local(path), b = Remote(p);
                int common = 0;
                foreach (string s in a) if (b.Contains(s)) common++;
                return common > 0 && common * 2 >= a.Count;
            }
            catch { return false; }
        }

        public static bool Twin(string path, PakPart p)
        {
            try
            {
                HashSet<string> a = Local(path), b = Remote(p);
                int common = 0;
                foreach (string s in a) if (b.Contains(s)) common++;
                return common > 0 && common * 5 >= a.Count * 3 && common * 5 >= b.Count * 3;
            }
            catch { return false; }
        }

        public static byte[] ReadEntry(string path, string wanted)
        {
            try
            {
                using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] h = Read(f, 12);
                    uint version = BitConverter.ToUInt32(h, 4), size = BitConverter.ToUInt32(h, 8);
                    if (BitConverter.ToUInt32(h, 0) != 0x55aa1234u || (version != 1 && version != 2) || size > (64u << 20)) return null;
                    int header = version == 2 ? 28 : 12;
                    if (version == 2) Read(f, 16);
                    byte[] t = Read(f, (int)size);
                    int i = 0;
                    while (true)
                    {
                        string ext = Str(t, ref i);
                        if (ext == "") break;
                        while (true)
                        {
                            string dir = Str(t, ref i);
                            if (dir == "") break;
                            while (true)
                            {
                                string name = Str(t, ref i);
                                if (name == "") break;
                                int preload = BitConverter.ToUInt16(t, i + 4), archive = BitConverter.ToUInt16(t, i + 6), pre = i + 18;
                                uint off = BitConverter.ToUInt32(t, i + 8), len = BitConverter.ToUInt32(t, i + 12);
                                i += 18 + preload;
                                string full = (dir == " " ? "" : dir + "/") + name + (ext == " " ? "" : "." + ext);
                                if (!string.Equals(full, wanted, StringComparison.OrdinalIgnoreCase)) continue;
                                if (archive != 0x7FFF) return null;
                                byte[] data = new byte[preload + len];
                                Array.Copy(t, pre, data, 0, preload);
                                f.Position = header + size + off;
                                int got = 0;
                                while (got < len) { int r = f.Read(data, preload + got, (int)len - got); if (r <= 0) return null; got += r; }
                                return data;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    sealed class Managers
    {
        readonly HashSet<string> dmm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, JObj> grimoire = new Dictionary<string, JObj>(StringComparer.OrdinalIgnoreCase);
        readonly string addons;
        public bool Dmm, Grimoire;
        static readonly Dictionary<string, string> shaCache = new Dictionary<string, string>();

        public Managers(string citadel)
        {
            addons = Path.Combine(citadel, "addons");
            try
            {
                string f = Path.Combine(addons, ".dmm.json");
                if (File.Exists(f))
                {
                    Dmm = true;
                    JObj mods = ((JObj)Json.Parse(File.ReadAllText(f, Encoding.UTF8))).Obj("mods");
                    if (mods != null) foreach (string id in mods.Keys)
                    {
                        List<object> files = mods.Obj(id) != null ? mods.Obj(id).Arr("currentVpks") : null;
                        if (files != null) foreach (object n in files) if (n is string) dmm.Add((string)n);
                    }
                }
            }
            catch (Exception e) { Log.Write("dmm: " + e.Message); }
            try
            {
                string dir = GrimoireDir;
                string settings = Path.Combine(dir, "settings.json"), meta = Path.Combine(dir, "mod-metadata.json");
                if (File.Exists(settings))
                {
                    string game = ((JObj)Json.Parse(File.ReadAllText(settings, Encoding.UTF8))).Str("deadlockPath");
                    Grimoire = string.Equals(Steam.CitadelOf(game), Path.GetFullPath(citadel), StringComparison.OrdinalIgnoreCase);
                }
                if (Grimoire && File.Exists(meta))
                {
                    JObj rows = (JObj)Json.Parse(File.ReadAllText(meta, Encoding.UTF8));
                    foreach (string k in rows.Keys) if (rows.Obj(k) != null && k.IndexOf('/') < 0) grimoire[k] = rows.Obj(k);
                }
            }
            catch (Exception e) { Log.Write("grimoire: " + e.Message); }
        }

        static string Sha256(string path)
        {
            FileInfo fi = new FileInfo(path);
            string key = path.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks, s;
            lock (shaCache) if (shaCache.TryGetValue(key, out s)) return s;
            using (SHA256 sha = SHA256.Create())
            using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 17))
                s = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
            lock (shaCache) shaCache[key] = s;
            return s;
        }

        public bool DmmHas(string name) { return dmm.Contains(name); }

        static string GrimoireDir
        {
            get { return Environment.GetEnvironmentVariable("COMPLOCK_GRIMOIRE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "grimoire"); }
        }

        static JObj ModInfo(string path)
        {
            try
            {
                byte[] raw = Vpk.ReadEntry(path, "modinfo.json");
                return raw != null ? Json.Parse(Encoding.UTF8.GetString(raw)) as JObj : null;
            }
            catch { return null; }
        }

        static string Identity(string path, JObj info)
        {
            string sha = info != null && info.Obj("identity") != null ? info.Obj("identity").Str("sha256") : null;
            return !string.IsNullOrEmpty(sha) ? sha : Sha256(path);
        }

        public static int NameInGrimoire(string citadel, IDictionary<string, string> moved, IEnumerable<string> ours)
        {
            Managers m = new Managers(citadel);
            if (!m.Grimoire) return 0;
            string file = Path.Combine(GrimoireDir, "mod-metadata.json");
            JObj rows = File.Exists(file) ? Json.Parse(File.ReadAllText(file, Encoding.UTF8)) as JObj : new JObj();
            if (rows == null) return 0;
            int changed = 0;
            foreach (KeyValuePair<string, string> kv in moved)
            {
                JObj row = rows.Obj(kv.Key);
                if (row == null) continue;
                rows.Remove(kv.Key);
                changed++;
                string now = Path.Combine(m.addons, kv.Value);
                if (File.Exists(now) && string.Equals(row.Str("sha256"), Identity(now, ModInfo(now)), StringComparison.OrdinalIgnoreCase)) rows[kv.Value] = row;
            }
            foreach (string name in ours)
            {
                string path = Path.Combine(m.addons, name);
                JObj info = File.Exists(path) ? ModInfo(path) : null;
                string title = info != null ? info.Str("title") : null;
                if (string.IsNullOrEmpty(title)) continue;
                string sha = Identity(path, info);
                JObj row = rows.Obj(name);
                if (row != null && row.Str("modName") == title && string.Equals(row.Str("sha256"), sha, StringComparison.OrdinalIgnoreCase)) continue;
                JObj fresh = new JObj();
                fresh["modName"] = title;
                if (!string.IsNullOrEmpty(info.Str("author"))) fresh["author"] = info.Str("author");
                fresh["sha256"] = sha;
                fresh["nsfw"] = false;
                fresh["imprinted"] = true;
                rows[name] = fresh;
                changed++;
            }
            if (changed == 0) return 0;
            Directory.CreateDirectory(GrimoireDir);
            string tmp = file + ".complock-tmp";
            File.WriteAllText(tmp, Json.Write(rows), new UTF8Encoding(false));
            if (File.Exists(file)) { File.Copy(file, file + ".complock-bak", true); File.Delete(file); }
            File.Move(tmp, file);
            Log.Write("grimoire: " + changed + " record" + (changed == 1 ? "" : "s") + " brought in line with the paks");
            return changed;
        }

        public string Who(string name)
        {
            if (dmm.Contains(name)) return "on record in Deadlock Mod Manager";
            if (!Grimoire) return null;
            try
            {
                JObj row;
                if (grimoire.TryGetValue(name, out row) && !string.IsNullOrEmpty(row.Str("modName"))
                    && string.Equals(row.Str("sha256"), Sha256(Path.Combine(addons, name)), StringComparison.OrdinalIgnoreCase))
                    return "in Grimoire as " + row.Str("modName");
            }
            catch { }
            return "in Grimoire's list";
        }
    }

    static class Video
    {
        public const string Title = "Recommended Video Settings";
        public const string Text = "Sets the quality options in your video.txt to the game's lowest: no ambient occlusion, bloom, anti-aliasing, upscaler, "
            + "depth of field or motion blur, low shadows, fog, textures and shaders, and the render scale to the one picked here. Resolution and window mode stay as they are.";
        public const int DefaultScale = 75;
        public static readonly int[] Scales = { 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100 };
        const string ScaleKey = "setting.mat_viewportscale";
        static string ScaleText(int percent) { return (percent / 100.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
        static bool ScaleIs(string value, int percent)
        {
            double v;
            return double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && Math.Abs(v - percent / 100.0) < 0.005;
        }
        static readonly string[][] Min = {
            new string[] { "setting.r_citadel_ssao_quality", "0" }, new string[] { "setting.r_effects_bloom", "false" }, new string[] { "setting.r_post_bloom", "false" },
            new string[] { "setting.r_citadel_antialiasing", "0" }, new string[] { "setting.r_citadel_upscaling", "0" }, new string[] { "setting.r_depth_of_field", "false" },
            new string[] { "setting.r_arealights", "false" }, new string[] { "setting.r_texture_stream_mip_bias", "2" }, new string[] { "setting.r_dashboard_render_quality", "0" },
            new string[] { "setting.shaderquality", "0" }, new string[] { "setting.r_citadel_fog_quality", "0" }, new string[] { "setting.r_citadel_motion_blur", "0" },
            new string[] { "setting.r_citadel_shadow_quality", "0" }, new string[] { "setting.useadvanced", "1" } };

        static string Norm(string v) { v = v.Trim().ToLowerInvariant(); return v == "false" ? "0" : v == "true" ? "1" : v; }
        static Regex Line(string key) { return new Regex("^([ \\t]*\"" + Regex.Escape(key) + "\"[ \\t]+)\"([^\"\\r\\n]*)\"", RegexOptions.Multiline | RegexOptions.IgnoreCase); }

        public static bool IsMin(string text, int scale)
        {
            Match s = Line(ScaleKey).Match(text);
            if (!s.Success || !ScaleIs(s.Groups[2].Value, scale)) return false;
            foreach (string[] kv in Min)
            {
                Match m = Line(kv[0]).Match(text);
                if (!m.Success || Norm(m.Groups[2].Value) != Norm(kv[1])) return false;
            }
            return true;
        }

        public static string ToMin(string text, int scale)
        {
            string eol = text.Contains("\r\n") ? "\r\n" : "\n";
            StringBuilder add = new StringBuilder();
            Match s = Line(ScaleKey).Match(text);
            if (!s.Success) add.Append("\t\"" + ScaleKey + "\"\t\t\"" + ScaleText(scale) + "\"" + eol);
            else if (!ScaleIs(s.Groups[2].Value, scale)) text = Line(ScaleKey).Replace(text, delegate (Match x) { return x.Groups[1].Value + "\"" + ScaleText(scale) + "\""; }, 1);
            foreach (string[] kv in Min)
            {
                Regex re = Line(kv[0]);
                string to = kv[1];
                Match m = re.Match(text);
                if (!m.Success) add.Append("\t\"" + kv[0] + "\"\t\t\"" + to + "\"" + eol);
                else if (Norm(m.Groups[2].Value) != Norm(to)) text = re.Replace(text, delegate (Match x) { return x.Groups[1].Value + "\"" + to + "\""; }, 1);
            }
            if (add.Length > 0)
            {
                int close = text.LastIndexOf('}');
                if (close < 0) throw new Exception("video.txt isn't in the form the game writes");
                text = text.Insert(text.LastIndexOf('\n', close) + 1, add.ToString());
            }
            return text;
        }

        static List<string> UserFiles()
        {
            List<string> found = new List<string>();
            foreach (string root in Steam.Roots())
            {
                try
                {
                    string ud = Path.Combine(root, "userdata");
                    if (!Directory.Exists(ud)) continue;
                    foreach (string acct in Directory.GetDirectories(ud))
                    {
                        string f = Path.Combine(acct, Steam.AppId + "\\local\\cfg\\video.txt");
                        if (File.Exists(f) && !found.Contains(f)) found.Add(f);
                    }
                }
                catch { }
            }
            found.Sort(delegate (string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            return found;
        }

        public static List<string> Targets(string citadel, string gameinfo, out string seed)
        {
            seed = null;
            List<string> user = UserFiles();
            if (gameinfo != null && Regex.IsMatch(gameinfo, "^[ \\t]*\"UserSettingsPathID\"[ \\t]+\"USRLOCAL\"", RegexOptions.Multiline | RegexOptions.IgnoreCase)) return user;
            string own = Path.Combine(citadel, "cfg\\video.txt");
            if (!File.Exists(own) && user.Count > 0) seed = user[0];
            List<string> one = new List<string>();
            one.Add(own);
            return one;
        }

        static void WriteKeepingReadOnly(string file, string text)
        {
            FileAttributes had = File.Exists(file) ? File.GetAttributes(file) : FileAttributes.Normal;
            if ((had & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, had & ~FileAttributes.ReadOnly);
            File.WriteAllText(file, text, new UTF8Encoding(false));
            if ((had & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, had);
        }

        public static bool Apply(string file, string seed, int scale)
        {
            string from = File.Exists(file) ? file : seed;
            if (from == null || !File.Exists(from)) return false;
            string text = File.ReadAllText(from, Encoding.UTF8), min = ToMin(text, scale);
            if (File.Exists(file) && min == text) return false;
            if (File.Exists(file) && !File.Exists(file + ".complock-bak")) File.Copy(file, file + ".complock-bak");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            WriteKeepingReadOnly(file, min);
            return true;
        }

        public static bool Restore(string citadel)
        {
            bool any = false;
            List<string> all = UserFiles();
            all.Add(Path.Combine(citadel, "cfg\\video.txt"));
            foreach (string f in all)
            {
                string bak = f + ".complock-bak";
                if (!File.Exists(bak)) continue;
                WriteKeepingReadOnly(f, File.ReadAllText(bak, Encoding.UTF8));
                File.Delete(bak);
                any = true;
            }
            return any;
        }
    }
}