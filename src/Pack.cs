using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CompLock
{
    sealed class PakPart
    {
        public string Name;
        public long Size, CSize;
        public uint Crc;
        public List<string> Urls = new List<string>();
    }

    sealed class PakFile
    {
        public string File, Title;
        public int Num;
        public List<PakPart> Parts = new List<PakPart>();
    }

    sealed class Resolved
    {
        public List<PakFile> Paks = new List<PakFile>();
        public JObj Lines = new JObj(), Off = new JObj();
        public long Down;
        public Dictionary<string, List<string>> ByItem = new Dictionary<string, List<string>>();
        public void Note(string id, string file)
        {
            if (!ByItem.ContainsKey(id)) ByItem[id] = new List<string>();
            if (!ByItem[id].Contains(file)) ByItem[id].Add(file);
        }
    }

    sealed class Pack
    {
        public readonly JObj D, Files;
        public readonly List<JObj> Base, Options, Groups;
        public readonly List<JObj> BaseChoice = new List<JObj>();
        public readonly string GameinfoText, Game;
        public readonly JObj Fov;
        public readonly List<string> FovValues = new List<string>();
        public readonly string FovDefault, FovCvar;
        public Dictionary<string, object> Sel = new Dictionary<string, object>();
        public bool LowVideo = true;
        public int RenderScale = Video.DefaultScale;
        readonly Dictionary<string, object> kept = new Dictionary<string, object>();
        readonly Dictionary<string, JObj> opt = new Dictionary<string, JObj>();

        public Pack(JObj d)
        {
            D = d;
            Base = d.Objs("base"); Options = d.Objs("option"); Groups = d.Objs("group");
            Files = d.Obj("files") ?? new JObj();
            GameinfoText = d.Str("gameinfo") ?? "";
            Game = d.Str("game") ?? "";
            foreach (JObj o in Options) opt[o.Str("id")] = o;
            foreach (JObj b in Base)
            {
                if (Choices(b).Count > 0) BaseChoice.Add(b);
                if (Fov == null && b.Obj("fov") != null) Fov = b.Obj("fov");
            }
            if (Fov != null)
            {
                FovCvar = Fov.Str("cvar"); FovDefault = Fov.Str("default");
                double from = Fov.Num("from", 0), to = Fov.Num("to", 0), by = Fov.Num("by", 1);
                FovValues.Add("0");
                for (int i = 0; from + i * by <= to + 1e-9; i++) FovValues.Add((from + i * by).ToString("0.00", CultureInfo.InvariantCulture));
            }
            Sel = Defaults();
        }

        public static List<JObj> Choices(JObj o) { return o.Objs("choice"); }
        public static JObj DefaultChoice(JObj o)
        {
            List<JObj> c = Choices(o);
            foreach (JObj x in c) if (x.Bool("default")) return x;
            return c.Count > 0 ? c[0] : null;
        }
        public JObj Option(string id) { JObj o; return opt.TryGetValue(id, out o) ? o : null; }
        object Get(string id) { object v; return Sel.TryGetValue(id, out v) ? v : null; }
        public bool IsOn(string id) { object v = Get(id); return v != null && !(v is bool && !(bool)v); }
        public bool Included(JObj o) { string i = o.Str("included"); return i != null && IsOn(i); }
        public string ChoiceId(string id) { return Get(id) as string; }
        public JObj Chosen(JObj o)
        {
            string id = ChoiceId(o.Str("id"));
            foreach (JObj c in Choices(o)) if (c.Str("id") == id) return c;
            return null;
        }

        public void SetOn(string id, bool on)
        {
            JObj o = Option(id);
            bool hasChoice = Choices(o).Count > 0;
            if (on)
            {
                object k;
                if (!hasChoice) Sel[id] = true;
                else if (Get(id) is string) { }
                else if (kept.TryGetValue(id, out k) && k is string) Sel[id] = k;
                else Sel[id] = DefaultChoice(o).Str("id");
            }
            else
            {
                if (hasChoice) kept[id] = Get(id);
                if (o.Has("gameinfo_else")) Sel[id] = false; else Sel.Remove(id);
            }
        }
        public void Choose(string id, string cid) { Sel[id] = cid; }
        public string FovValue() { return Fov == null ? null : (Get("fov") as string ?? FovDefault); }
        public void SetFov(string v) { if (v == FovDefault) Sel.Remove("fov"); else Sel["fov"] = v; }

        Dictionary<string, object> WithBase(Dictionary<string, object> s)
        {
            foreach (JObj b in BaseChoice)
            {
                object v; s.TryGetValue(b.Str("id"), out v);
                bool found = false;
                foreach (JObj c in Choices(b)) if (c.Str("id") == v as string) found = true;
                if (!found) s[b.Str("id")] = DefaultChoice(b).Str("id");
            }
            return s;
        }

        public Dictionary<string, object> Defaults()
        {
            Dictionary<string, object> s = new Dictionary<string, object>();
            foreach (JObj o in Options)
                if (o.Bool("on")) s[o.Str("id")] = Choices(o).Count > 0 ? (object)DefaultChoice(o).Str("id") : true;
            return WithBase(s);
        }

        public string Encode()
        {
            List<string> p = new List<string>();
            foreach (JObj b in BaseChoice)
                if (ChoiceId(b.Str("id")) != DefaultChoice(b).Str("id")) p.Add(b.Str("id") + ":" + ChoiceId(b.Str("id")));
            foreach (JObj o in Options)
                if (IsOn(o.Str("id")) && !o.Has("gameinfo_else")) p.Add(Get(o.Str("id")) is bool ? o.Str("id") : o.Str("id") + ":" + Get(o.Str("id")));
            foreach (JObj o in Options)
                if (o.Has("gameinfo_else") && !IsOn(o.Str("id"))) p.Add(o.Str("id") + ":off");
            if (Fov != null && Get("fov") is string) p.Add("fov:" + Get("fov"));
            return string.Join(",", p.ToArray());
        }

        public Dictionary<string, object> Decode(string text)
        {
            Dictionary<string, object> s = new Dictionary<string, object>();
            foreach (JObj o in Options) if (o.Has("gameinfo_else")) s[o.Str("id")] = true;
            foreach (string part in (text ?? "").Split(','))
            {
                string[] kv = part.Split(':');
                string id = kv[0], cid = kv.Length > 1 ? kv[1] : null;
                bool off = cid == "off" || cid == "false";
                if (id == "fov") { if (Fov != null && FovValues.Contains(cid) && cid != FovDefault) s["fov"] = cid; continue; }
                JObj o = Option(id);
                if (o == null) foreach (JObj b in BaseChoice) if (b.Str("id") == id) o = b;
                if (o == null) continue;
                if (o.Has("gameinfo_else")) s[id] = !off;
                else if (Choices(o).Count > 0)
                {
                    JObj pick = DefaultChoice(o);
                    foreach (JObj c in Choices(o)) if (c.Str("id") == cid) pick = c;
                    s[id] = pick.Str("id");
                }
                else if (!off) s[id] = true;
            }
            return WithBase(s);
        }

        static void AddPaks(List<string> files, JObj x)
        {
            string p = x.Str("pak");
            if (p != null && !files.Contains(p)) files.Add(p);
            List<object> more = x.Arr("paks");
            if (more != null) foreach (object m in more) if (m is string && !files.Contains((string)m)) files.Add((string)m);
        }

        public PakFile FileOf(string name)
        {
            JObj f = Files.Obj(name);
            if (f == null) throw new Exception("the pack names " + name + ", which the site doesn't list");
            PakFile p = new PakFile();
            p.File = name; p.Title = f.Str("title"); p.Num = (int)f.Long("num");
            foreach (JObj q in f.Objs("parts"))
            {
                PakPart part = new PakPart();
                part.Name = q.Str("name"); part.Size = q.Long("size"); part.CSize = q.Long("csize"); part.Crc = (uint)q.Long("crc");
                if (q["url"] is string) part.Urls.Add((string)q["url"]);
                else foreach (object u in q.Arr("url")) part.Urls.Add((string)u);
                p.Parts.Add(part);
            }
            return p;
        }

        public Resolved Resolve()
        {
            Resolved r = new Resolved();
            List<string> files = new List<string>(), flags = new List<string>();
            foreach (JObj o in Options) if (o.Str("flag") != null && IsOn(o.Str("id"))) flags.Add(o.Str("flag"));
            foreach (JObj b in BaseChoice)
            {
                JObj c = Chosen(b) ?? DefaultChoice(b);
                if (c.Str("flag") != null) flags.Add(c.Str("flag"));
                r.Lines.Assign(c.Obj("gameinfo"));
                r.Off.Assign(c.Obj("gameinfo_off"));
            }
            foreach (JObj b in Base)
            {
                r.Lines.Assign(b.Obj("gameinfo"));
                List<string> sorted = new List<string>(flags);
                sorted.Sort(string.CompareOrdinal);
                string key = string.Join("+", sorted.ToArray());
                string pak = b.Str("pak");
                if (pak != null)
                {
                    JObj variants = b.Obj("variants");
                    string v = key != "" && variants != null ? variants.Str(key) : null;
                    if (!files.Contains(v ?? pak)) files.Add(v ?? pak);
                    r.Note(b.Str("id"), v ?? pak);
                }
                List<object> more = b.Arr("paks");
                if (more != null) foreach (object m in more) if (m is string) { if (!files.Contains((string)m)) files.Add((string)m); r.Note(b.Str("id"), (string)m); }
            }
            if (Fov != null && Get("fov") is string) r.Lines[FovCvar] = Get("fov");
            foreach (JObj o in Options)
            {
                string id = o.Str("id");
                if (!IsOn(id)) r.Lines.Assign(o.Obj("gameinfo_else"));
                if (!IsOn(id) || Included(o)) continue;
                JObj c = Chosen(o);
                foreach (JObj x in new JObj[] { o, c })
                {
                    if (x == null) continue;
                    AddPaks(files, x);
                    if (x.Str("pak") != null) r.Note(id, x.Str("pak"));
                    if (x.Arr("paks") != null) foreach (object m in x.Arr("paks")) if (m is string) r.Note(id, (string)m);
                    r.Lines.Assign(x.Obj("gameinfo"));
                    r.Off.Assign(x.Obj("gameinfo_off"));
                }
            }
            foreach (string k in r.Lines.Keys) r.Off.Remove(k);
            foreach (string f in files) r.Paks.Add(FileOf(f));
            r.Paks.Sort(delegate (PakFile a, PakFile b) { return a.Num.CompareTo(b.Num); });
            for (int i = 1; i < r.Paks.Count; i++)
                if (r.Paks[i].Num == r.Paks[i - 1].Num) throw new Exception("two paks with the same number: " + r.Paks[i - 1].File + ", " + r.Paks[i].File);
            foreach (PakFile p in r.Paks) foreach (PakPart q in p.Parts) r.Down += q.CSize;
            return r;
        }

        static double Pct(object v)
        {
            Match m = Regex.Match(v as string ?? "", "^([+\u2212-]?)(\\d+(?:\\.\\d+)?)%$");
            if (!m.Success) return 0;
            string sign = m.Groups[1].Value;
            return (sign != "" && sign != "+" ? -1 : 1) * double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100;
        }
        static double N(JObj x, string k) { return Pct(x["est_" + k] as string ?? x[k]); }
        static bool Times(double[] into, JObj x, JObj by)
        {
            bool changed = false;
            string[] keys = { "avg", "low" };
            for (int i = 0; i < 2; i++)
            {
                double f = (1 + N(x, keys[i])) / (by != null ? 1 + N(by, keys[i]) : 1);
                into[i] *= f;
                changed = changed || f != 1;
            }
            return changed;
        }

        public double[] Estimate()
        {
            double[] b = { 1, 1 }, more = { 1, 1 };
            int count = 0;
            foreach (JObj x in Base)
            {
                Times(b, x, null);
                if (Choices(x).Count > 0 && Times(more, Chosen(x) ?? DefaultChoice(x), DefaultChoice(x))) count++;
            }
            foreach (JObj o in Options)
            {
                if (!IsOn(o.Str("id")) || Included(o)) continue;
                JObj c = Chosen(o);
                if (Times(more, c != null && (c.Str("avg") != null || c.Str("low") != null) ? c : o, null)) count++;
            }
            JObj s = D.Obj("est_scale");
            double n = s != null ? s.Num("n", 0) : 0;
            double t = s != null && n > 1 ? Math.Min(1, Math.Max(0, count - 1) / (n - 1)) : 0;
            double pa = s != null ? 1 + (s.Num("avg", 1) - 1) * t : 1, pl = s != null ? 1 + (s.Num("low", 1) - 1) * t : 1;
            return new double[] { b[0] * Math.Pow(more[0], pa) - 1, b[1] * Math.Pow(more[1], pl) - 1 };
        }
        public static string Signed(double x) { return (x < 0 ? "\u2212" : "+") + (Math.Abs(x) * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }

        public static string Gameinfo(string text, JObj convars, JObj off)
        {
            Match m = Regex.Match(text, "^(\\t*)ConVars[ \\t]*\\r?\\n\\1\\{[ \\t]*\\r?\\n", RegexOptions.Multiline);
            if (!m.Success) throw new Exception("gameinfo.gi has no ConVars block");
            string tabs = m.Groups[1].Value;
            int start = m.Index + m.Length, end = text.IndexOf("\n" + tabs + "}", start, StringComparison.Ordinal);
            if (end < 0) throw new Exception("gameinfo.gi's ConVars block doesn't end");
            string block = text.Substring(start, end - start);
            StringBuilder add = new StringBuilder();
            foreach (string k in convars.Keys)
            {
                string name = "\"" + Regex.Escape(k) + "\"", v = (string)convars[k];
                Regex re = new Regex("^(\\s*" + name + "\\s+)\"[^\"\\r\\n]*\"", RegexOptions.Multiline);
                Regex commented = new Regex("^([ \\t]*)//[ \\t]*(" + name + "\\s+)\"[^\"\\r\\n]*\"", RegexOptions.Multiline);
                if (re.IsMatch(block)) block = re.Replace(block, delegate (Match x) { return x.Groups[1].Value + "\"" + v + "\""; }, 1);
                else if (commented.IsMatch(block)) block = commented.Replace(block, delegate (Match x) { return x.Groups[1].Value + x.Groups[2].Value + "\"" + v + "\""; }, 1);
                else add.Append(tabs + "\t\"" + k + "\" \"" + v + "\"\n");
            }
            if (off != null) foreach (string k in off.Keys)
            {
                if (convars.Has(k)) continue;
                string name = "\"" + Regex.Escape(k) + "\"";
                Regex re = new Regex("^([ \\t]*)(" + name + "\\s+\"[^\"\\r\\n]*\")", RegexOptions.Multiline);
                if (re.IsMatch(block)) block = re.Replace(block, delegate (Match x) { return x.Groups[1].Value + "// " + x.Groups[2].Value; }, 1);
                else add.Append(tabs + "\t// \"" + k + "\" \"" + (string)off[k] + "\"\n");
            }
            string head = add.Length > 0 ? tabs + "\t// CompLock Pack Builder\n" + add + "\n" : "";
            return text.Substring(0, start) + head + block + text.Substring(end);
        }

        public string ShareUrl() { string e = Encode(); return Web.Base + "/?pack=" + (e != "" ? e : "none") + "#build"; }

        public string ModsTxt(Resolved r)
        {
            List<string> l = new List<string>();
            l.Add("CompLock pack, made with the Pack Builder. pak01 loads first, and a lower number wins when two paks");
            l.Add("change the same file. Delete these files to remove it, then verify the game files in Steam.");
            l.Add("");
            foreach (PakFile p in r.Paks)
            {
                List<string> names = new List<string>();
                foreach (PakPart q in p.Parts) names.Add(q.Name);
                l.Add(string.Join(" + ", names.ToArray()) + "  " + p.Title + "  (" + p.File + ")");
            }
            l.Add("");
            l.Add("gameinfo.gi: CompLock's" + (r.Lines.Count + r.Off.Count > 0 ? ", with" : ""));
            foreach (string k in r.Lines.Keys) l.Add("  \"" + k + "\" \"" + r.Lines[k] + "\"");
            foreach (string k in r.Off.Keys) l.Add("  // \"" + k + "\" \"" + r.Off[k] + "\"  (commented out)");
            l.Add("");
            l.Add("This pack in the Pack Builder: " + ShareUrl());
            l.Add("");
            return string.Join("\r\n", l.ToArray());
        }

        public static List<string> Listed(string modsTxt)
        {
            List<string> r = new List<string>();
            if (string.IsNullOrEmpty(modsTxt) || !modsTxt.StartsWith("CompLock", StringComparison.Ordinal)) return r;
            foreach (string line in modsTxt.Split('\n'))
            {
                if (!Regex.IsMatch(line, "^pak\\d\\d_")) continue;
                string first = line.Split(new string[] { "  " }, StringSplitOptions.None)[0];
                foreach (string n in first.Split(new string[] { " + " }, StringSplitOptions.None))
                    if (Regex.IsMatch(n.Trim(), "^pak\\d\\d_(dir|\\d{3})\\.vpk$")) r.Add(n.Trim());
            }
            return r;
        }
        public static string LinkOf(string modsTxt)
        {
            if (string.IsNullOrEmpty(modsTxt) || !modsTxt.StartsWith("CompLock", StringComparison.Ordinal)) return null;
            Match m = Regex.Match(modsTxt, "\\?pack=([^#\\s]*)");
            if (!m.Success) return null;
            return m.Groups[1].Value == "none" ? "" : m.Groups[1].Value;
        }
    }
}
