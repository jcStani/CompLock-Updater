using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CompLock
{
    sealed class JObj
    {
        public readonly List<string> Keys = new List<string>();
        readonly Dictionary<string, object> map = new Dictionary<string, object>();

        public object this[string k]
        {
            get { object v; return map.TryGetValue(k, out v) ? v : null; }
            set { if (!map.ContainsKey(k)) Keys.Add(k); map[k] = value; }
        }
        public bool Has(string k) { return map.ContainsKey(k); }
        public void Remove(string k) { if (map.Remove(k)) Keys.Remove(k); }
        public int Count { get { return Keys.Count; } }
        public string Str(string k) { return this[k] as string; }
        public JObj Obj(string k) { return this[k] as JObj; }
        public List<object> Arr(string k) { return this[k] as List<object>; }
        public bool Bool(string k) { object v = this[k]; return v is bool && (bool)v; }
        public double Num(string k, double fallback) { object v = this[k]; return v is double ? (double)v : fallback; }
        public long Long(string k) { return (long)Num(k, 0); }
        public void Assign(JObj other) { if (other != null) foreach (string k in other.Keys) this[k] = other[k]; }
        public List<JObj> Objs(string k)
        {
            List<JObj> r = new List<JObj>();
            List<object> a = Arr(k);
            if (a != null) foreach (object o in a) if (o is JObj) r.Add((JObj)o);
            return r;
        }
    }

    static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Skip(s, ref i);
            if (i != s.Length) throw new FormatException("JSON: text after the value at " + i);
            return v;
        }

        static void Skip(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: ended early");
            char c = s[i];
            if (c == '{')
            {
                JObj o = new JObj();
                i++; Skip(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    string k = Str(s, ref i);
                    Skip(s, ref i);
                    if (s[i++] != ':') throw new FormatException("JSON: no ':' at " + i);
                    o[k] = Value(s, ref i);
                    Skip(s, ref i);
                    char d = s[i++];
                    if (d == '}') return o;
                    if (d != ',') throw new FormatException("JSON: no ',' at " + i);
                }
            }
            if (c == '[')
            {
                List<object> a = new List<object>();
                i++; Skip(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    char d = s[i++];
                    if (d == ']') return a;
                    if (d != ',') throw new FormatException("JSON: no ',' at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int j = i;
            while (j < s.Length && "+-0123456789.eE".IndexOf(s[j]) >= 0) j++;
            if (j == i) throw new FormatException("JSON: unexpected '" + c + "' at " + i);
            double n = double.Parse(s.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture);
            i = j;
            return n;
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: no string at " + i);
            StringBuilder b = new StringBuilder();
            i++;
            while (true)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                c = s[i++];
                switch (c)
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append('\t'); break;
                    case 'r': b.Append('\r'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u': b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: b.Append(c); break;
                }
            }
        }

        public static string Write(object v)
        {
            StringBuilder b = new StringBuilder();
            Write(b, v, 0);
            b.Append("\n");
            return b.ToString();
        }

        static void Write(StringBuilder b, object v, int depth)
        {
            if (v == null) { b.Append("null"); return; }
            if (v is bool) { b.Append((bool)v ? "true" : "false"); return; }
            if (v is string) { Quote(b, (string)v); return; }
            if (v is JObj)
            {
                JObj o = (JObj)v;
                if (o.Count == 0) { b.Append("{}"); return; }
                b.Append("{\n");
                for (int k = 0; k < o.Keys.Count; k++)
                {
                    b.Append(' ', depth + 1);
                    Quote(b, o.Keys[k]);
                    b.Append(": ");
                    Write(b, o[o.Keys[k]], depth + 1);
                    b.Append(k + 1 < o.Keys.Count ? ",\n" : "\n");
                }
                b.Append(' ', depth).Append('}');
                return;
            }
            if (v is List<object>)
            {
                List<object> a = (List<object>)v;
                b.Append('[');
                for (int k = 0; k < a.Count; k++) { if (k > 0) b.Append(", "); Write(b, a[k], depth + 1); }
                b.Append(']');
                return;
            }
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (d == Math.Floor(d) && Math.Abs(d) < 9e15) b.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            else b.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void Quote(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                if (c == '"') b.Append("\\\"");
                else if (c == '\\') b.Append("\\\\");
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            b.Append('"');
        }
    }
}
