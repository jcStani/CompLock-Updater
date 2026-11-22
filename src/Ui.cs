using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CompLock
{
    static class Th
    {
        public static readonly Color Bg = Color.FromArgb(0x0e, 0x0e, 0x0e), Panel = Color.FromArgb(0x17, 0x17, 0x17), Panel2 = Color.FromArgb(0x24, 0x24, 0x24),
            Line = Color.FromArgb(0x2c, 0x2c, 0x2c), Fg = Color.FromArgb(0xf5, 0xf5, 0xf5), Muted = Color.FromArgb(0xa3, 0xa3, 0xa3), Accent = Color.FromArgb(0xd9, 0xa4, 0x41),
            AccentInk = Color.FromArgb(0x15, 0x12, 0x0a), Knob = Color.FromArgb(0xfa, 0xfa, 0xfa), Track = Color.FromArgb(0x38, 0x38, 0x38), LineOn = Color.FromArgb(0x40, 0x40, 0x40),
            Ok = Color.FromArgb(0x4a, 0xde, 0x80), Caution = Color.FromArgb(0xf8, 0x71, 0x71), Good = Color.FromArgb(0x22, 0xc5, 0x5e), Update = Color.FromArgb(0xd9, 0xa4, 0x41),
            UpdateInk = Color.FromArgb(0x15, 0x12, 0x0a), UpdateTint = Color.FromArgb(0x2b, 0x24, 0x13);
        public static float S = 1;
        public static Font Body, Small, Bold, Big, Title, Caps, Button;
        const TextFormatFlags TF = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        public static void Init(float dpi)
        {
            S = dpi / 96f;
            Body = new Font("Segoe UI", 9.5f); Small = new Font("Segoe UI", 8.25f); Bold = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            Big = new Font("Segoe UI", 15f, FontStyle.Bold); Title = new Font("Segoe UI", 17f, FontStyle.Bold); Caps = new Font("Segoe UI Semibold", 7.75f);
            Button = new Font("Segoe UI", 9f, FontStyle.Bold);
        }
        public static int Px(float v) { return (int)Math.Round(v * S); }
        public static Size Measure(Graphics g, string s, Font f) { return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue), TF); }
        public static Size Measure(Graphics g, string s, Font f, int width) { return TextRenderer.MeasureText(g, s, f, new Size(width, int.MaxValue), TF | TextFormatFlags.WordBreak); }
        public static void Text(Graphics g, bool paint, string s, Font f, Color c, int x, int y) { if (paint) TextRenderer.DrawText(g, s, f, new Point(x, y), c, TF); }
        public static void Wrap(Graphics g, bool paint, string s, Font f, Color c, Rectangle r) { if (paint) TextRenderer.DrawText(g, s, f, r, c, TF | TextFormatFlags.WordBreak); }
        public static void Clip(Graphics g, bool paint, string s, Font f, Color c, Rectangle r) { if (paint) TextRenderer.DrawText(g, s, f, r, c, TF | TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine); }
        public static void Tail(Graphics g, bool paint, string s, Font f, Color c, Rectangle r) { if (paint) TextRenderer.DrawText(g, s, f, r, c, TF | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine); }
        public static string Few(List<string> l) { return l.Count <= 2 ? string.Join(", ", l.ToArray()) : l[0] + ", " + l[1] + " and " + (l.Count - 2) + " more"; }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static void Box(Graphics g, Rectangle r, int rad, Color fill, Color border)
        {
            using (GraphicsPath p = Round(r, rad))
            {
                if (fill != Color.Empty) using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (border != Color.Empty) using (Pen pen = new Pen(border, Math.Max(1f, S))) g.DrawPath(pen, p);
            }
        }
        public static void Toggle(Graphics g, Rectangle r, bool on)
        {
            Box(g, r, r.Height / 2, on ? Good : Track, Color.Empty);
            int k = r.Height - Px(6);
            using (SolidBrush b = new SolidBrush(on ? Knob : Muted))
                g.FillEllipse(b, on ? r.Right - k - Px(3) : r.X + Px(3), r.Y + Px(3), k, k);
        }
        public static string Bytes(long n)
        {
            if (n >= 1 << 20) { double v = n / 1048576.0; return v.ToString(v < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " MB"; }
            if (n >= 1 << 10) { double v = n / 1024.0; return v.ToString(v < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " KB"; }
            return n + " B";
        }
        public static Color Tone(string v) { return v != null && v.StartsWith("+") ? Ok : v != null && (v.StartsWith("−") || v.StartsWith("-")) ? Caution : Muted; }
    }

    class Canvas : Control
    {
        struct Hit { public Rectangle R; public Action Do; }
        readonly List<Hit> hits = new List<Hit>();

        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Th.Bg;
        }
        protected void Hot(Rectangle r, bool paint, Action a) { if (paint) { Hit h; h.R = r; h.Do = a; hits.Add(h); } }
        protected virtual int Draw(Graphics g, int w, bool paint) { return 0; }
        public int Measure(int w) { using (Graphics g = CreateGraphics()) return Draw(g, w, false); }
        protected override void OnPaint(PaintEventArgs e)
        {
            hits.Clear();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            Draw(e.Graphics, Width, true);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = false;
            foreach (Hit h in hits) if (h.R.Contains(e.Location)) over = true;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Press(e.Location, false);
            base.OnMouseDown(e);
        }
        public bool Press(Point at, bool redraw)
        {
            if (redraw) using (Graphics g = CreateGraphics()) { hits.Clear(); Draw(g, Width, true); }
            Hit[] all = hits.ToArray();
            for (int i = all.Length - 1; i >= 0; i--) if (all[i].R.Contains(at)) { all[i].Do(); return true; }
            return false;
        }
    }

    sealed class GroupHead : Canvas
    {
        readonly string title, text;
        public GroupHead(string title, string text) { this.title = title; this.text = text; }
        protected override int Draw(Graphics g, int w, bool paint)
        {
            int y = Th.Px(14);
            Th.Text(g, paint, title.ToUpperInvariant(), Th.Caps, Th.Fg, Th.Px(2), y);
            int tw = Th.Measure(g, title.ToUpperInvariant(), Th.Caps).Width;
            if (!string.IsNullOrEmpty(text)) Th.Tail(g, paint, text, Th.Small, Th.Muted, new Rectangle(Th.Px(2) + tw + Th.Px(10), y - Th.Px(1), w - tw - Th.Px(14), Th.Px(18)));
            return y + Th.Px(22);
        }
    }

    enum RowState { None, Installed, Update }

    sealed class Row : Canvas
    {
        readonly MainForm host;
        readonly JObj item;
        readonly bool isBase;
        public Row(MainForm host, JObj item, bool isBase) { this.host = host; this.item = item; this.isBase = isBase; }
        public string Id { get { return item.Str("id"); } }
        public Point UpdateSpot;

        protected override int Draw(Graphics g, int w, bool paint)
        {
            Pack pack = host.Pack;
            string id = item.Str("id");
            bool app = item.Bool("app");
            bool on = isBase || (app ? pack.LowVideo : pack.IsOn(id) || pack.Included(item));
            List<JObj> choices = Pack.Choices(item);
            JObj chosen = on ? pack.Chosen(item) : null;
            int P = Th.Px(14), sw = Th.Px(38), sh = Th.Px(20), x = P + sw + Th.Px(12), right = w - P, y = P;
            if (paint) Th.Box(g, new Rectangle(0, 0, w - 1, Height - 1), Th.Px(10), Th.Panel, on && !isBase ? Th.LineOn : Th.Line);

            Rectangle sr = new Rectangle(P, y + Th.Px(1), sw, sh);
            if (paint && !isBase) Th.Toggle(g, sr, on);
            if (paint && isBase)
            {
                Rectangle c = new Rectangle(P + Th.Px(9), y + Th.Px(1), sh, sh);
                using (Pen pen = new Pen(Th.Good, Th.Px(2)))
                {
                    g.DrawEllipse(pen, c);
                    g.DrawLines(pen, new Point[] { new Point(c.X + Th.Px(5), c.Y + Th.Px(10)), new Point(c.X + Th.Px(9), c.Y + Th.Px(14)), new Point(c.X + Th.Px(15), c.Y + Th.Px(6)) });
                }
            }

            JObj nums = !isBase && chosen != null && (chosen.Str("avg") != null || chosen.Str("low") != null) ? chosen : item;
            int nx = right;
            foreach (string[] k in new string[][] { new string[] { "low", "1% low" }, new string[] { "avg", "avg" } })
            {
                string v = nums.Str(k[0]);
                if (v == null) continue;
                int lw = Th.Measure(g, k[1], Th.Small).Width, vw = Th.Measure(g, v, Th.Bold).Width;
                nx -= lw; Th.Text(g, paint, k[1], Th.Small, Th.Muted, nx, y + Th.Px(4));
                nx -= vw + Th.Px(4); Th.Text(g, paint, v, Th.Bold, on ? Th.Tone(v) : Th.Muted, nx, y);
                nx -= Th.Px(14);
            }

            string title = item.Str("title") ?? id;
            Size ts = Th.Measure(g, title, Th.Bold);
            Th.Text(g, paint, title, Th.Bold, on ? Th.Fg : Th.Muted, x, y);
            if (!isBase) Hot(new Rectangle(0, 0, Math.Max(x + ts.Width, sr.Right) + P, y + ts.Height + Th.Px(6)), paint, delegate { if (app) host.SetVideo(!pack.LowVideo); else host.SetOn(id, !pack.IsOn(id)); });
            int ax = x + ts.Width + Th.Px(10);

            JObj upd = item.Obj("updated");
            if (upd != null && upd.Str("text") != null)
            {
                string tag = ("Updated " + upd.Str("text")).ToUpperInvariant();
                Size us = Th.Measure(g, tag, Th.Caps);
                bool fresh = upd.Bool("new");
                int tagW = fresh ? us.Width + Th.Px(24) : us.Width;
                if (ax + tagW < nx)
                {
                    Rectangle tr = new Rectangle(ax, y + Th.Px(1), tagW, us.Height + Th.Px(6));
                    if (fresh)
                    {
                        if (paint)
                        {
                            Th.Box(g, tr, tr.Height / 2, Th.UpdateTint, Color.Empty);
                            using (SolidBrush b = new SolidBrush(Th.Update)) g.FillEllipse(b, tr.X + Th.Px(8), tr.Y + (tr.Height - Th.Px(6)) / 2, Th.Px(6), Th.Px(6));
                        }
                        Th.Text(g, paint, tag, Th.Caps, Th.Update, tr.X + Th.Px(18), tr.Y + Th.Px(3));
                    }
                    else Th.Text(g, paint, tag, Th.Caps, Th.Muted, tr.X, tr.Y + Th.Px(3));
                    string href = upd.Str("href");
                    if (href != null) Hot(tr, paint, delegate { MainForm.Open(Web.Base + href); });
                    ax += tagW + Th.Px(10);
                }
            }
            string by = item.Str("by");
            if (by != null && ax + Th.Measure(g, by, Th.Small).Width < nx) Th.Text(g, paint, by, Th.Small, Th.Muted, ax, y + Th.Px(4));
            y += ts.Height + Th.Px(4);

            RowState state = host.StateOf(item, isBase);
            int aside = 0, floor = y;
            if (state == RowState.Update)
            {
                int bw = Th.Px(84), bh = Th.Px(26);
                Rectangle ur = new Rectangle(right - bw, y + Th.Px(1), bw, bh);
                if (paint) Th.Box(g, ur, Th.Px(6), Th.Update, Color.Empty);
                Size bs = Th.Measure(g, "Update", Th.Button);
                Th.Text(g, paint, "Update", Th.Button, Th.UpdateInk, ur.X + (bw - bs.Width) / 2, ur.Y + (bh - bs.Height) / 2);
                Hot(ur, paint, delegate { host.UpdateRow(item, isBase); });
                if (paint) UpdateSpot = new Point(ur.X + bw / 2, ur.Y + bh / 2);
                aside = bw + Th.Px(14); floor = ur.Bottom + Th.Px(4);
            }
            else if (state == RowState.Installed)
            {
                Size ss = Th.Measure(g, "Installed", Th.Small);
                Th.Text(g, paint, "Installed", Th.Small, Th.Ok, right - ss.Width, y + Th.Px(2));
                aside = ss.Width + Th.Px(14);
            }

            int tw = right - x - aside;
            string text = pack.Included(item) && !pack.IsOn(id) ? "Part of " + (pack.Option(item.Str("included")).Str("title")) + "." : item.Str("text");
            if (!string.IsNullOrEmpty(text))
            {
                int h = Th.Measure(g, text, Th.Body, tw).Height;
                Th.Wrap(g, paint, text, Th.Body, Th.Muted, new Rectangle(x, y, tw, h));
                y += h + Th.Px(4);
            }
            string warn = (chosen != null ? chosen.Str("warn") : null) ?? item.Str("warn");
            if (!string.IsNullOrEmpty(warn))
            {
                int h = Th.Measure(g, warn, Th.Small, tw).Height;
                Th.Wrap(g, paint, warn, Th.Small, Th.Caution, new Rectangle(x, y, tw, h));
                y += h + Th.Px(4);
            }
            y = Math.Max(y, floor);
            tw = right - x;

            if (choices.Count > 0)
            {
                y += Th.Px(4);
                string label = item.Str("choice_label");
                int cx = x, ph = Th.Px(28);
                if (label != null)
                {
                    Th.Text(g, paint, label.ToUpperInvariant(), Th.Caps, Th.Muted, cx, y + Th.Px(8));
                    cx += Th.Measure(g, label.ToUpperInvariant(), Th.Caps).Width + Th.Px(12);
                }
                int start = cx;
                foreach (JObj c in choices)
                {
                    string ct = c.Str("title") ?? c.Str("id"), cid = c.Str("id");
                    int pw = Th.Measure(g, ct, Th.Body).Width + Th.Px(24);
                    if (cx + pw > right && cx > start) { cx = start; y += ph + Th.Px(6); }
                    Rectangle pr = new Rectangle(cx, y, pw, ph);
                    bool sel = on && chosen != null && chosen.Str("id") == cid;
                    if (paint) Th.Box(g, pr, Th.Px(6), sel ? Th.Panel2 : Color.Empty, sel ? Th.Accent : Th.Line);
                    Th.Text(g, paint, ct, Th.Body, sel ? Th.Fg : Th.Muted, pr.X + Th.Px(12), pr.Y + (ph - Th.Measure(g, ct, Th.Body).Height) / 2);
                    Hot(pr, paint, delegate { host.Choose(id, cid); });
                    cx += pw + Th.Px(8);
                }
                y += ph + Th.Px(4);
                string ctext = chosen != null ? chosen.Str("text") : null;
                if (!string.IsNullOrEmpty(ctext))
                {
                    int h = Th.Measure(g, ctext, Th.Small, tw).Height;
                    Th.Wrap(g, paint, ctext, Th.Small, Th.Muted, new Rectangle(x, y + Th.Px(2), tw, h));
                    y += h + Th.Px(6);
                }
            }

            if (app)
            {
                y += Th.Px(4);
                int v = pack.RenderScale, i = Array.IndexOf(Video.Scales, v), bh = Th.Px(28), bw = Th.Px(30), vw = Th.Px(150);
                Th.Text(g, paint, "RENDER SCALE", Th.Caps, on ? Th.Muted : Th.Line, x, y + Th.Px(8));
                int fx = x + Th.Measure(g, "RENDER SCALE", Th.Caps).Width + Th.Px(12);
                Rectangle l = new Rectangle(fx, y, bw, bh), mid = new Rectangle(fx + bw + Th.Px(6), y, vw, bh), r = new Rectangle(mid.Right + Th.Px(6), y, bw, bh);
                bool down = on && i > 0, up = on && i >= 0 && i < Video.Scales.Length - 1;
                if (paint)
                {
                    Th.Box(g, l, Th.Px(6), Color.Empty, Th.Line); Th.Box(g, r, Th.Px(6), Color.Empty, Th.Line); Th.Box(g, mid, Th.Px(6), Th.Panel2, Th.Line);
                    using (Pen pen = new Pen(down ? Th.Fg : Th.Line, Th.Px(2)))
                        g.DrawLines(pen, new Point[] { new Point(l.X + bw / 2 + Th.Px(3), l.Y + Th.Px(8)), new Point(l.X + bw / 2 - Th.Px(3), l.Y + bh / 2), new Point(l.X + bw / 2 + Th.Px(3), l.Bottom - Th.Px(8)) });
                    using (Pen pen = new Pen(up ? Th.Fg : Th.Line, Th.Px(2)))
                        g.DrawLines(pen, new Point[] { new Point(r.X + bw / 2 - Th.Px(3), r.Y + Th.Px(8)), new Point(r.X + bw / 2 + Th.Px(3), r.Y + bh / 2), new Point(r.X + bw / 2 - Th.Px(3), r.Bottom - Th.Px(8)) });
                }
                string shown = v + "%" + (v == Video.DefaultScale ? "  (default)" : "");
                Size ss = Th.Measure(g, shown, Th.Body);
                Th.Text(g, paint, shown, Th.Body, on ? Th.Fg : Th.Muted, mid.X + (vw - ss.Width) / 2, mid.Y + (bh - ss.Height) / 2);
                if (down) Hot(l, paint, delegate { host.SetScale(Video.Scales[i - 1]); });
                if (up) Hot(r, paint, delegate { host.SetScale(Video.Scales[i + 1]); });
                y += bh + Th.Px(2);
            }

            if (item.Obj("fov") != null && pack.Fov != null)
            {
                y += Th.Px(4);
                string v = pack.FovValue();
                int i = pack.FovValues.IndexOf(v), bh = Th.Px(28), bw = Th.Px(30), vw = Th.Px(150);
                Th.Text(g, paint, "FIELD OF VIEW", Th.Caps, Th.Muted, x, y + Th.Px(8));
                int fx = x + Th.Measure(g, "FIELD OF VIEW", Th.Caps).Width + Th.Px(12);
                Rectangle l = new Rectangle(fx, y, bw, bh), mid = new Rectangle(fx + bw + Th.Px(6), y, vw, bh), r = new Rectangle(mid.Right + Th.Px(6), y, bw, bh);
                if (paint)
                {
                    Th.Box(g, l, Th.Px(6), Color.Empty, Th.Line); Th.Box(g, r, Th.Px(6), Color.Empty, Th.Line); Th.Box(g, mid, Th.Px(6), Th.Panel2, Th.Line);
                    using (Pen pen = new Pen(i > 0 ? Th.Fg : Th.Line, Th.Px(2)))
                        g.DrawLines(pen, new Point[] { new Point(l.X + bw / 2 + Th.Px(3), l.Y + Th.Px(8)), new Point(l.X + bw / 2 - Th.Px(3), l.Y + bh / 2), new Point(l.X + bw / 2 + Th.Px(3), l.Bottom - Th.Px(8)) });
                    using (Pen pen = new Pen(i < pack.FovValues.Count - 1 ? Th.Fg : Th.Line, Th.Px(2)))
                        g.DrawLines(pen, new Point[] { new Point(r.X + bw / 2 - Th.Px(3), r.Y + Th.Px(8)), new Point(r.X + bw / 2 + Th.Px(3), r.Y + bh / 2), new Point(r.X + bw / 2 - Th.Px(3), r.Bottom - Th.Px(8)) });
                }
                string shown = v == "0" ? "Stock (your screen's)" : v == pack.FovDefault ? v + "  (CompLock's)" : v;
                Size ss = Th.Measure(g, shown, Th.Body);
                Th.Text(g, paint, shown, Th.Body, Th.Fg, mid.X + (vw - ss.Width) / 2, mid.Y + (bh - ss.Height) / 2);
                if (i > 0) Hot(l, paint, delegate { host.SetFov(pack.FovValues[i - 1]); });
                if (i >= 0 && i < pack.FovValues.Count - 1) Hot(r, paint, delegate { host.SetFov(pack.FovValues[i + 1]); });
                y += bh + Th.Px(2);
            }
            return y + P - Th.Px(2);
        }
    }

    sealed class Head : Canvas
    {
        readonly MainForm host;
        public Head(MainForm host) { this.host = host; }
        protected override int Draw(Graphics g, int w, bool paint)
        {
            int P = Th.Px(20), y = Th.Px(14);
            Th.Text(g, paint, "CompLock", Th.Title, Th.Fg, P, y);
            int tw = Th.Measure(g, "CompLock", Th.Title).Width;
            string sub = "By jcStani and HoppCX  ·  " + Build.Date;
            Th.Text(g, paint, sub, Th.Body, Th.Muted, P + tw + Th.Px(12), y + Th.Px(11));
            string site = Web.Base.Replace("https://", "");
            Size ss = Th.Measure(g, site, Th.Body);
            Th.Text(g, paint, site, Th.Body, Th.Accent, w - P - ss.Width, y + Th.Px(11));
            Hot(new Rectangle(w - P - ss.Width, y + Th.Px(6), ss.Width, ss.Height + Th.Px(8)), paint, delegate { MainForm.Open(Web.Base + "/"); });
            y += Th.Px(44);
            Th.Text(g, paint, "GAME FOLDER", Th.Caps, Th.Muted, P, y + Th.Px(3));
            int lx = P + Th.Measure(g, "GAME FOLDER", Th.Caps).Width + Th.Px(12);
            string link = host.Citadel != null ? "Change" : "Choose";
            Size ls = Th.Measure(g, link, Th.Body);
            string path = host.Citadel != null ? Path.GetFullPath(Path.Combine(host.Citadel, "..\\..")) : "Not found. Pick the folder Steam opens with Manage › Browse local files.";
            Th.Clip(g, paint, path, Th.Body, host.Citadel != null ? Th.Fg : Th.Caution, new Rectangle(lx, y, w - P - ls.Width - Th.Px(16) - lx, Th.Px(20)));
            Th.Text(g, paint, link, Th.Body, Th.Accent, w - P - ls.Width, y);
            Hot(new Rectangle(w - P - ls.Width - Th.Px(6), y - Th.Px(4), ls.Width + Th.Px(12), ls.Height + Th.Px(8)), paint, delegate { host.PickFolder(); });
            if (paint) using (Pen pen = new Pen(Th.Line)) g.DrawLine(pen, 0, Height - 1, w, Height - 1);
            return Th.Px(96);
        }
    }

    sealed class Foot : Canvas
    {
        readonly MainForm host;
        public Point ButtonSpot;
        public Foot(MainForm host) { this.host = host; BackColor = Th.Panel; }
        protected override int Draw(Graphics g, int w, bool paint)
        {
            int P = Th.Px(20), y = Th.Px(14);
            if (paint) using (Pen pen = new Pen(Th.Line)) g.DrawLine(pen, 0, 0, w, 0);
            if (paint && host.Busy && host.Total > 0)
                using (SolidBrush b = new SolidBrush(Th.Accent)) g.FillRectangle(b, 0, 0, (int)(w * Math.Min(1.0, host.Got / (double)host.Total)), Th.Px(3));

            Color sc; string status = host.Status(out sc);
            Size ms = Th.Measure(g, "More", Th.Body);
            Th.Tail(g, paint, status, Th.Body, sc, new Rectangle(P, y, w - 2 * P - ms.Width - Th.Px(16), Th.Px(20)));
            Th.Text(g, paint, "More", Th.Body, Th.Accent, w - P - ms.Width, y);
            Rectangle more = new Rectangle(w - P - ms.Width - Th.Px(6), y - Th.Px(4), ms.Width + Th.Px(12), ms.Height + Th.Px(8));
            Hot(more, paint, delegate { host.ShowMore(this, new Point(more.Left, more.Bottom)); });
            y += Th.Px(30);

            int bw = Th.Px(170), bh = Th.Px(44);
            Rectangle br = new Rectangle(w - P - bw, y, bw, bh);
            bool can; string label = host.Button(out can);
            bool updates = label == "Update all";
            if (paint) Th.Box(g, br, Th.Px(8), can ? (updates ? Th.Update : Th.Accent) : Th.Panel2, can ? Color.Empty : Th.Line);
            Size bs = Th.Measure(g, label, Th.Bold);
            Th.Text(g, paint, label, Th.Bold, can ? (updates ? Th.UpdateInk : Th.AccentInk) : Th.Muted, br.X + (bw - bs.Width) / 2, br.Y + (bh - bs.Height) / 2);
            if (can) Hot(br, paint, delegate { host.ButtonClicked(); });
            if (paint) ButtonSpot = new Point(br.X + bw / 2, br.Y + bh / 2);

            if (host.Pack != null)
            {
                double[] e = host.Pack.Estimate();
                Th.Text(g, paint, "ESTIMATED FPS GAIN", Th.Caps, Th.Muted, P, y);
                int ex = P, ey = y + Th.Px(14);
                foreach (string[] k in new string[][] { new string[] { Pack.Signed(e[0]), "avg" }, new string[] { Pack.Signed(e[1]), "1% low" } })
                {
                    Th.Text(g, paint, k[0], Th.Big, Th.Tone(k[0]), ex, ey);
                    ex += Th.Measure(g, k[0], Th.Big).Width + Th.Px(5);
                    Th.Text(g, paint, k[1], Th.Small, Th.Muted, ex, ey + Th.Px(11));
                    ex += Th.Measure(g, k[1], Th.Small).Width + Th.Px(18);
                }
            }
            return y + bh + Th.Px(16);
        }
    }

    sealed class MainForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public Pack Pack;
        public string Citadel;
        public bool Busy;
        public long Got, Total;
        Check check;
        bool live, tooOld;
        string loadError, note;
        Color noteColor;
        int generation;
        readonly Settings settings = Settings.Load();
        readonly Head head;
        readonly Foot foot;
        readonly Panel list = new Panel();
        readonly Timer tick = new Timer(), settle = new Timer(), poll = new Timer();
        readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        readonly bool shot;
        DateTime siteAt;
        string siteRaw, baseCode;
        bool siteBusy, siteNewer;

        public MainForm(string shotFolder, string shotPack)
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Th.Init(g.DpiX);
            shot = shotFolder != null || shotPack != null;
            Text = "CompLock";
            BackColor = Th.Bg; ForeColor = Th.Fg;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Th.Px(780), Math.Min(Th.Px(900), Screen.PrimaryScreen.WorkingArea.Height - Th.Px(60)));
            MinimumSize = new Size(Th.Px(620), Th.Px(520));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            head = new Head(this); foot = new Foot(this);
            head.Dock = DockStyle.Top; head.Height = Th.Px(96);
            foot.Dock = DockStyle.Bottom; foot.Height = Th.Px(104);
            list.Dock = DockStyle.Fill; list.AutoScroll = true; list.BackColor = Th.Bg;
            Controls.Add(list); Controls.Add(foot); Controls.Add(head);
            list.Resize += delegate { Arrange(); };
            list.HandleCreated += delegate { try { SetWindowTheme(list.Handle, "DarkMode_Explorer", null); } catch { } };
            tick.Interval = 100;
            tick.Tick += delegate { if (Busy) foot.Invalidate(); };
            tick.Start();
            settle.Interval = 800;
            settle.Tick += delegate { settle.Stop(); Auto(false); };
            poll.Interval = 300000;
            poll.Tick += delegate { Auto(true); };
            if (!shot) poll.Start();
            if (shot) LoadNow(shotFolder, shotPack);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); if (!shot) Reload(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); if (foot != null) Auto(true); }
        protected override void OnFormClosed(FormClosedEventArgs e) { Unwatch(); base.OnFormClosed(e); }

        void Auto(bool site)
        {
            if (shot || Busy || Pack == null || Citadel == null) return;
            Recheck(true);
            if (site) CheckSite();
        }

        void CheckSite()
        {
            if (siteBusy || (DateTime.UtcNow - siteAt).TotalMinutes < 5) return;
            siteBusy = true;
            Task.Factory.StartNew(delegate
            {
                bool lv; string err;
                Web.Load(20000, out lv, out err);
                string raw = lv ? Web.LastRaw : null;
                Post(delegate
                {
                    siteBusy = false; siteAt = DateTime.UtcNow;
                    if (raw == null || raw == siteRaw || Busy || Pack == null) return;
                    if (baseCode != null && Pack.Encode() != baseCode) { siteNewer = true; foot.Invalidate(); return; }
                    Reload();
                });
            });
        }

        void Unwatch()
        {
            foreach (FileSystemWatcher w in watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch { } }
            watchers.Clear();
        }

        void Watch()
        {
            Unwatch();
            if (shot || Pack == null || Citadel == null) return;
            List<string[]> spots = new List<string[]>();
            spots.Add(new string[] { Citadel, "gameinfo.gi" });
            spots.Add(new string[] { Path.Combine(Citadel, "addons"), "*.*" });
            if (check != null) foreach (string f in check.VideoFiles) spots.Add(new string[] { Path.GetDirectoryName(f), "video.txt" });
            foreach (string[] s in spots)
            {
                try
                {
                    if (!Directory.Exists(s[0])) continue;
                    FileSystemWatcher w = new FileSystemWatcher(s[0], s[1]);
                    w.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
                    w.SynchronizingObject = this;
                    FileSystemEventHandler changed = delegate { if (!Busy) { settle.Stop(); settle.Start(); } };
                    w.Changed += changed; w.Created += changed; w.Deleted += changed;
                    w.Renamed += delegate { if (!Busy) { settle.Stop(); settle.Start(); } };
                    w.EnableRaisingEvents = true;
                    watchers.Add(w);
                }
                catch (Exception e) { Log.Write("watch: " + e.Message); }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            list.AutoScrollPosition = new Point(0, -list.AutoScrollPosition.Y - e.Delta * Th.Px(70) / 120);
            base.OnMouseWheel(e);
        }
        static int Scale(State st) { return st.Scale != null && Array.IndexOf(Video.Scales, (int)st.Scale) >= 0 ? (int)st.Scale : Video.DefaultScale; }
        void Post(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        public static void Open(string what) { try { Process.Start(what); } catch { } }

        void LoadNow(string folder, string code)
        {
            JObj data = Web.Load(20000, out live, out loadError);
            Citadel = !string.IsNullOrEmpty(folder) ? Steam.CitadelOf(folder) : settings.Citadel();
            if (data == null) return;
            tooOld = data.Num("app_min", 0) > Web.AppVersion;
            Pack = new Pack(data);
            if (Citadel != null) { State st = State.Load(Path.Combine(Citadel, "addons")); Pack.LowVideo = st.Video != false; Pack.RenderScale = Scale(st); }
            Dictionary<string, object> s = code != null ? Pack.Decode(code == "none" ? "" : code) : Citadel != null ? Installer.Installed(Pack, Citadel) : null;
            if (s != null) Pack.Sel = s;
            if (Citadel != null) check = Installer.Look(Pack, Citadel);
            Build();
        }

        void Reload()
        {
            Pack = null; check = null; loadError = null; note = null;
            Build();
            int gen = ++generation;
            Task.Factory.StartNew(delegate
            {
                bool lv; string err;
                JObj data = Web.Load(20000, out lv, out err);
                string raw = lv ? Web.LastRaw : null;
                string cit = settings.Citadel();
                Pack p = null; Check c = null; string fail = err;
                try
                {
                    if (data != null)
                    {
                        p = new Pack(data);
                        if (cit != null) { State st = State.Load(Path.Combine(cit, "addons")); p.LowVideo = st.Video != false; p.RenderScale = Scale(st); }
                        Dictionary<string, object> s = cit != null ? Installer.Installed(p, cit) : null;
                        if (s != null) p.Sel = s;
                        if (cit != null) c = Installer.Look(p, cit);
                    }
                }
                catch (Exception e) { fail = e.Message; Log.Write("load: " + e); }
                Post(delegate
                {
                    if (gen != generation) return;
                    live = lv; loadError = fail; Citadel = cit; Pack = p; check = c;
                    if (raw != null) siteRaw = raw;
                    siteAt = DateTime.UtcNow; siteNewer = false;
                    baseCode = p != null ? p.Encode() : null;
                    tooOld = data != null && data.Num("app_min", 0) > Web.AppVersion;
                    Build();
                });
            });
        }

        void Recheck(bool quiet = false)
        {
            if (!quiet) { check = null; foot.Invalidate(); }
            if (Pack == null || Citadel == null) return;
            int gen = ++generation;
            Pack p = Pack; string cit = Citadel;
            Task.Factory.StartNew(delegate
            {
                Check c = null; string fail = null;
                try { lock (p) c = Installer.Look(p, cit); } catch (Exception e) { fail = e.Message; Log.Write("look: " + e); }
                Post(delegate
                {
                    if (gen != generation) return;
                    check = c;
                    if (fail != null) Say(fail, Th.Caution);
                    Arrange();
                    foot.Invalidate();
                });
            });
        }

        void Build()
        {
            list.SuspendLayout();
            foreach (Control c in new List<Control>(ToList(list.Controls))) { list.Controls.Remove(c); c.Dispose(); }
            if (Pack != null)
            {
                list.Controls.Add(new GroupHead("Always in your pack", Pack.Game != "" ? "For Deadlock build " + Pack.Game + "." : ""));
                foreach (JObj b in Pack.Base) list.Controls.Add(new Row(this, b, true));
                foreach (JObj grp in Pack.Groups)
                {
                    bool any = false;
                    foreach (JObj o in Pack.Options)
                    {
                        if (o.Str("group") != grp.Str("id")) continue;
                        if (!any) list.Controls.Add(new GroupHead(grp.Str("title") ?? "", grp.Str("text")));
                        any = true;
                        list.Controls.Add(new Row(this, o, false));
                    }
                }
                JObj video = new JObj();
                video["id"] = "video"; video["app"] = true; video["title"] = Video.Title; video["text"] = Video.Text;
                list.Controls.Add(new GroupHead("This PC", "Not part of the pack: the game's own settings file."));
                list.Controls.Add(new Row(this, video, false));
            }
            list.ResumeLayout();
            Arrange();
            head.Invalidate(); foot.Invalidate();
            Watch();
        }
        static IEnumerable<Control> ToList(Control.ControlCollection cc) { foreach (Control c in cc) yield return c; }

        void Arrange()
        {
            int pad = Th.Px(20), w = list.ClientSize.Width - 2 * pad, y = Th.Px(4) + list.AutoScrollPosition.Y;
            if (w < 50) return;
            foreach (Control c in list.Controls)
            {
                Canvas cv = c as Canvas;
                if (cv == null) continue;
                int h = cv.Measure(w);
                cv.SetBounds(pad, y, w, h);
                y += h + (cv is Row ? Th.Px(8) : 0);
                cv.Invalidate();
            }
            list.AutoScrollMargin = new Size(0, Th.Px(14));
        }

        void Changed() { note = null; Arrange(); Recheck(); }
        public void SetOn(string id, bool on) { if (Busy) return; lock (Pack) Pack.SetOn(id, on); Changed(); }
        public void Choose(string id, string cid) { if (Busy) return; lock (Pack) Pack.Choose(id, cid); Changed(); }
        public void SetFov(string v) { if (Busy) return; lock (Pack) Pack.SetFov(v); Changed(); }
        public void SetVideo(bool on) { if (Busy) return; lock (Pack) Pack.LowVideo = on; Changed(); }
        public void SetScale(int percent) { if (Busy) return; lock (Pack) Pack.RenderScale = percent; Changed(); }

        public void PickFolder()
        {
            if (Busy) return;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Deadlock's folder: the one Steam opens with Manage › Browse local files";
                d.ShowNewFolderButton = false;
                if (Citadel != null) d.SelectedPath = Path.GetFullPath(Path.Combine(Citadel, "..\\.."));
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string c = Steam.CitadelOf(d.SelectedPath);
                if (c == null) { Say("That isn't Deadlock's folder. Pick the one Steam opens with Manage › Browse local files.", Th.Caution); return; }
                settings.Game = c; settings.Save();
                Reload();
            }
        }

        void Say(string s, Color c) { note = s; noteColor = c; foot.Invalidate(); }

        public RowState StateOf(JObj item, bool isBase)
        {
            Check c = check;
            if (c == null || Busy) return RowState.None;
            if (item.Bool("app")) return c.VideoUpdate ? RowState.Update : c.VideoOk ? RowState.Installed : RowState.None;
            if (isBase && item.Str("pak") == null)
                return c.GameinfoUpdate ? RowState.Update : c.WasGameinfo != null ? RowState.Installed : RowState.None;
            List<string> files;
            if (!c.R.ByItem.TryGetValue(item.Str("id"), out files)) return RowState.None;
            foreach (PakPart p in c.Updates) if (files.Contains(c.Owner[p].File)) return RowState.Update;
            foreach (string f in files) if (!c.Current.Contains(f)) return RowState.None;
            return RowState.Installed;
        }

        public string Status(out Color c)
        {
            c = Th.Muted;
            if (Busy) { c = Th.Fg; return Total > 0 ? "Working… " + Th.Bytes(Got) + " of " + Th.Bytes(Total) : "Working…"; }
            if (Pack == null) { if (loadError != null) { c = Th.Caution; return "Couldn't load the pack from " + Web.Base.Replace("https://", "") + ": " + loadError; } return "Checking " + Web.Base.Replace("https://", "") + "…"; }
            if (tooOld) { c = Th.Caution; return "This app is too old for the current pack. Get the new one from " + Web.Base.Replace("https://", "") + "."; }
            if (note != null) { c = noteColor; return note; }
            string offline = live ? "" : "  ·  The site can't be reached: this is the last pack that loaded.";
            if (siteNewer) offline += "  ·  The site has changes: More › Check for updates now.";
            if (Citadel == null) { c = Th.Caution; return "Deadlock's folder wasn't found. Choose it above."; }
            if (check == null) return "Checking the game folder…";
            if (check.UpToDate) { c = Th.Ok; return "Up to date." + (check.Kept.Count > 0 ? "  Kept as yours: " + string.Join(", ", check.Kept.ToArray()) + "." : "") + offline; }
            c = Th.Fg;
            if (!check.Pending)
            {
                List<string> ups = check.Titles(check.Updates);
                if (check.GameinfoUpdate) ups.Add("gameinfo.gi");
                if (check.VideoUpdate) ups.Add(Video.Title);
                long d = check.DownOf(check.Updates);
                return "Updates available: " + Th.Few(ups) + "." + (d > 0 ? "  ·  " + Th.Bytes(d) : "") + offline;
            }
            List<string> ins = check.Titles(check.Installs);
            if (check.VideoOn && check.VideoPending) ins.Add(Video.Title);
            long down = check.DownOf(check.Installs);
            string tail = (down > 0 ? "  ·  " + Th.Bytes(down) + " to download" : "") + (check.HasUpdates ? "  ·  Updates are on their rows" : "") + offline;
            if (!check.Installed) return "Not installed yet." + tail;
            string s = (ins.Count > 0 ? "To install: " + Th.Few(ins) + ".  " : "") + (check.Stale.Count > 0 ? "To remove: " + Th.Few(check.Stale) + ".  " : "")
                + (check.OldCopies.Count > 0 ? "Other copies of the pack's mods: " + Th.Few(check.OldCopies) + ".  " : "");
            if (s == "") s = check.Gameinfo != Gi.Ok && check.GameinfoPending ? "gameinfo.gi changes with your selection." : "Apply saves this selection in the game folder.";
            return s.TrimEnd() + tail;
        }

        public string Button(out bool can)
        {
            can = false;
            if (Busy) return "Working…";
            if (Pack == null) { can = loadError != null; return can ? "Try again" : "Install"; }
            if (tooOld) return "Install";
            if (Citadel == null) { can = true; return "Choose folder"; }
            if (check == null) return "Checking…";
            if (check.UpToDate) return "Up to date";
            can = true;
            if (check.Pending) return check.Installed ? "Apply" : "Install";
            return "Update all";
        }

        public void ButtonClicked()
        {
            if (Busy) return;
            if (Pack == null) { Reload(); return; }
            if (Citadel == null) { PickFolder(); return; }
            if (check == null || check.UpToDate) return;
            if (check.Pending) Start(Plan.Apply(check), "Installed. Start Deadlock.");
            else Start(Plan.Update(check, null, false), "Updated. Start Deadlock.");
        }

        public void UpdateRow(JObj item, bool isBase)
        {
            if (Busy || check == null) return;
            Start(Plan.Update(check, item.Str("id"), isBase && item.Str("pak") == null), "Updated " + (item.Str("title") ?? item.Str("id")) + ".");
        }

        static string[] Labelled(Check c, List<string> names)
        {
            List<string> l = new List<string>();
            foreach (string n in names) l.Add(c.Who.ContainsKey(n) ? n + "   (" + c.Who[n] + ")" : n);
            return l.ToArray();
        }

        void Start(Plan plan, string done)
        {
            if (Steam.Running(Citadel)) { Say("Deadlock is running. Close it, then press the button again.", Th.Caution); return; }
            Check c = check;
            List<string> changed = new List<string>(), foreign = new List<string>();
            foreach (string n in c.Changed) if (plan.Only == null || plan.Only.Contains(n)) changed.Add(n);
            foreach (string n in c.Foreign) if (plan.Only == null || plan.Only.Contains(n)) foreign.Add(n);
            if (changed.Count > 0)
            {
                DialogResult r = MessageBox.Show(this, "These files were changed outside CompLock since it installed them:\n\n    " + string.Join("\n    ", changed.ToArray())
                    + "\n\nYes: replace them with the site's.\nNo: keep mine. CompLock leaves them alone for as long as they stay as they are.", "CompLock", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.No) plan.Keep.AddRange(changed);
            }
            List<string> alike = new List<string>();
            foreach (string n in foreign) if (c.LookAlike.Contains(n)) alike.Add(n);
            if (alike.Count > 0)
            {
                DialogResult r = MessageBox.Show(this, "These look like other copies of mods in the pack (they replace mostly the same game files):\n\n    " + string.Join("\n    ", Labelled(c, alike))
                    + "\n\nYes: replace them with the pack's.\nNo: keep them too, moved to free numbers. The pack's win where both change a file."
                    + "\nCancel: change nothing, for example to turn that option off in the pack and keep your own.", "CompLock", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) foreach (string n in alike) { plan.Replace.Add(n); foreign.Remove(n); }
            }
            if (foreign.Count > 0)
            {
                bool managed = false;
                foreach (string n in foreign) if (c.Who.ContainsKey(n)) managed = true;
                DialogResult r = MessageBox.Show(this, "Other mods in addons use file names the pack needs:\n\n    " + string.Join("\n    ", Labelled(c, foreign))
                    + "\n\nThey will be moved to free numbers after the pack's, so they keep working. Where they change the same file as the pack, the pack wins."
                    + (managed ? "\n\nA mod manager knows its mods by file name. After the move it will show the moved ones as unknown, and may show the pack's files under"
                        + " their old names: don't remove or update those entries there. Close the manager first, then let it look at the folder again." : ""),
                    "CompLock", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (r != DialogResult.OK) return;
            }
            if (plan.Cleanup && c.OldCopies.Count > 0)
            {
                List<string> lines = new List<string>();
                foreach (string n in c.OldCopies) lines.Add(n + "   (like " + c.OldOf[n] + (c.Who.ContainsKey(n) ? "; " + c.Who[n] : "") + ")");
                DialogResult r = MessageBox.Show(this, "These look like other copies of mods in the pack, under other numbers:\n\n    " + string.Join("\n    ", lines.ToArray())
                    + "\n\nYes: remove them. The pack installs its own.\nNo: keep them. Where two paks change the same file, the lower number wins.\nCancel: change nothing.",
                    "CompLock", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes) plan.Drop.AddRange(c.OldCopies); else plan.Keep.AddRange(c.OldCopies);
            }
            if (plan.Gameinfo != null && c.PerfPreset != null)
            {
                DialogResult r = MessageBox.Show(this, "gameinfo.gi has " + c.PerfPreset + "'s performance settings in it. This replaces them with the pack's."
                    + "\n\nTurn that feature off in " + c.PerfPreset + ": it puts its settings back when they are gone, and the pack's would then show as an update again.",
                    "CompLock", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (r != DialogResult.OK) return;
            }
            Busy = true; Got = 0; Total = 0; note = null;
            Arrange();
            foot.Invalidate();
            Pack p = Pack;
            Task.Factory.StartNew(delegate
            {
                string fail = null; Installer.Result res = null; Check after = null;
                try
                {
                    lock (p)
                    {
                        res = Installer.Install(p, c, plan, delegate (long got, long total) { Got = got; Total = total; });
                        after = Installer.Look(p, c.Citadel);
                    }
                }
                catch (Exception e) { fail = e.Message; Log.Write("install: " + e); }
                Post(delegate
                {
                    Busy = false;
                    generation++;
                    if (fail != null) { Recheck(); Arrange(); Say(fail + (fail.EndsWith(".") ? "" : "."), Th.Caution); return; }
                    check = after;
                    if (after != null && after.InstalledCode != null) baseCode = after.InstalledCode;
                    string s = done;
                    if (res.Removed.Count > 0) s += "  Removed: " + string.Join(", ", res.Removed.ToArray()) + ".";
                    if (res.Moved.Count > 0) s += "  Moved: " + string.Join(", ", res.Moved.ToArray()) + ".";
                    if (res.Video) s += "  Video settings are set.";
                    if (res.Named > 0) s += "  Grimoire's names for the pack were corrected.";
                    if (res.BackedUp) s += "  The gameinfo.gi from before is kept as gameinfo.gi.complock-bak.";
                    Arrange();
                    Say(s, Th.Ok);
                });
            });
        }

        public void ShowMore(Control at, Point where)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Check for updates now", null, delegate { if (!Busy) Reload(); });
            m.Items.Add("Open the addons folder", null, delegate { if (Citadel != null) Open(Path.Combine(Citadel, "addons")); }).Enabled = Citadel != null;
            if (Citadel != null && AnyKept())
                m.Items.Add("Stop keeping my own files", null, delegate
                {
                    try
                    {
                        State st = State.Load(Path.Combine(Citadel, "addons"));
                        foreach (string n in new List<string>(st.Files.Keys)) if (st.Files.Obj(n).Bool("keep")) st.Files.Remove(n);
                        st.Save(Path.Combine(Citadel, "addons"));
                        Recheck();
                    }
                    catch (Exception e) { Say(e.Message, Th.Caution); }
                });
            if (Citadel != null && check != null && new Managers(Citadel).Grimoire)
                m.Items.Add("Correct the pack's names in Grimoire", null, delegate
                {
                    try
                    {
                        List<string> ours = new List<string>();
                        foreach (PakFile pf in check.R.Paks)
                            foreach (PakPart p in pf.Parts)
                                if (check.Current.Contains(pf.File) && !check.Kept.Contains(p.Name)) ours.Add(p.Name);
                        int n = Managers.NameInGrimoire(Citadel, new Dictionary<string, string>(), ours);
                        Say(n > 0 ? "Grimoire's names for the pack were corrected. It shows them when it next looks at the folder." : "Grimoire already has the pack's names.", Th.Ok);
                    }
                    catch (Exception e) { Say(e.Message, Th.Caution); }
                });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Remove CompLock from the game…", null, delegate { RemoveClicked(); }).Enabled = Citadel != null && Pack != null && !Busy;
            m.Items.Add("Open the log", null, delegate { Open(Path.Combine(Web.DataDir, "log.txt")); });
            m.Show(at, where);
        }

        bool AnyKept()
        {
            try
            {
                State st = State.Load(Path.Combine(Citadel, "addons"));
                foreach (string n in st.Files.Keys) if (st.Files.Obj(n).Bool("keep")) return true;
            }
            catch { }
            return false;
        }

        void RemoveClicked()
        {
            if (MessageBox.Show(this, "Remove the pack's files from the game folder?\n\nOther mods in addons are left alone.", "CompLock",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                bool restored;
                Installer.Remove(Pack, Citadel, out restored);
                Recheck();
                Say(restored ? "Removed. gameinfo.gi is the one from before the pack again." : "Removed. To get the game's own gameinfo.gi back, verify the game files in Steam.", Th.Ok);
            }
            catch (Exception e) { Say(e.Message + ".", Th.Caution); }
        }

        public static int Shot(string file, string folder, string code)
        {
            using (MainForm f = new MainForm(folder ?? "", code))
            {
                string h = Environment.GetEnvironmentVariable("COMPLOCK_SHOT_H");
                if (!string.IsNullOrEmpty(h)) f.ClientSize = new Size(f.ClientSize.Width, int.Parse(h));
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, 0);
                f.ShowInTaskbar = false;
                f.Show();
                f.Arrange();
                Application.DoEvents();
                foreach (string step in (Environment.GetEnvironmentVariable("COMPLOCK_SHOT_DO") ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    bool took = false;
                    if (step == "bottom") { f.list.AutoScrollPosition = new Point(0, 100000); took = true; }
                    else if (step == "button") { f.foot.Press(new Point(-9, -9), true); took = f.foot.Press(f.foot.ButtonSpot, false); }
                    else foreach (Control c in f.list.Controls)
                    {
                        Row r = c as Row;
                        if (r == null) continue;
                        if ("toggle:" + r.Id == step) took = r.Press(new Point(Th.Px(30), Th.Px(24)), true);
                        if ("update:" + r.Id == step) { r.UpdateSpot = new Point(-9, -9); r.Press(r.UpdateSpot, true); took = r.Press(r.UpdateSpot, false); }
                    }
                    Console.WriteLine(step + ": " + (took ? "clicked" : "nothing to click"));
                    DateTime end = DateTime.Now.AddSeconds(180);
                    do { Application.DoEvents(); System.Threading.Thread.Sleep(20); } while ((f.Busy || (f.check == null && f.Citadel != null)) && DateTime.Now < end);
                }
                f.Arrange();
                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                {
                    foreach (Control c in new Control[] { f.list, f.foot, f.head })
                        using (Bitmap part = new Bitmap(c.Width, c.Height))
                        {
                            c.DrawToBitmap(part, new Rectangle(0, 0, c.Width, c.Height));
                            using (Graphics g = Graphics.FromImage(bmp)) g.DrawImage(part, c.Left, c.Top);
                        }
                    bmp.Save(file, ImageFormat.Png);
                }
            }
            return 0;
        }
    }
}
