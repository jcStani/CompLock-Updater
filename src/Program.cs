using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("CompLock")]
[assembly: AssemblyProduct("CompLock Updater")]
[assembly: AssemblyDescription("Installs the CompLock pack for Deadlock and shows when a mod has an update")]
[assembly: AssemblyCompany("jcStani and HoppCX")]
[assembly: AssemblyCopyright("Copyright (c) 2026 jcStani and HoppCX")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace CompLock
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Web.Init();
            string mode = args.Length > 0 ? args[0] : "";
            try
            {
                if (mode == "--gameinfo") return Gameinfo(args);
                if (mode == "--find")
                {
                    string at = args.Length > 1 ? Steam.CitadelOf(args[1], true) : Steam.FindGame();
                    Console.WriteLine("found: " + (at ?? "nothing" + (args.Length > 1 ? " (" + Steam.Why(args[1]) + ")" : "")));
                    return at != null ? 0 : 3;
                }
                if (mode == "--look" || mode == "--install" || mode == "--apply" || mode == "--update" || mode == "--remove" || mode == "--grimoire") return Headless(args);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
                {
                    Log.Write("error: " + e.Exception);
                    if (mode != "--shot") MessageBox.Show(e.Exception.Message, "CompLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (mode == "--shot") return MainForm.Shot(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);
                Application.Run(new MainForm(null, null));
                return 0;
            }
            catch (Exception e)
            {
                Log.Write("error: " + e);
                Console.Error.WriteLine(e.Message);
                if (args.Length == 0) MessageBox.Show(e.Message, "CompLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Gameinfo(string[] a)
        {
            Pack pack = new Pack((JObj)Json.Parse(File.ReadAllText(a[1], Encoding.UTF8)));
            if (a[2] != "@default") pack.Sel = pack.Decode(a[2] == "none" ? "" : a[2]);
            Resolved r = pack.Resolve();
            List<string> paks = new List<string>();
            foreach (PakFile p in r.Paks) paks.Add(p.File);
            double[] est = pack.Estimate();
            JObj o = new JObj();
            o["code"] = pack.Encode();
            o["paks"] = string.Join(",", paks.ToArray());
            o["down"] = (double)r.Down;
            o["est"] = Pack.Signed(est[0]) + " " + Pack.Signed(est[1]);
            o["gameinfo"] = Pack.Gameinfo(pack.GameinfoText, r.Lines, r.Off);
            o["mods"] = pack.ModsTxt(r);
            File.WriteAllText(a[3], Json.Write(o), new UTF8Encoding(false));
            return 0;
        }

        static int Headless(string[] a)
        {
            string citadel = Steam.CitadelOf(a[1]);
            if (citadel == null) throw new Exception(a[1] + " isn't a Deadlock folder");
            bool live; string err;
            JObj data = Web.Load(20000, out live, out err);
            if (data == null) throw new Exception("no pack data: " + err);
            Pack pack = new Pack(data);
            pack.LowVideo = State.Load(Path.Combine(citadel, "addons")).Video != false && Environment.GetEnvironmentVariable("COMPLOCK_VIDEO") != "0";
            int scale;
            pack.RenderScale = int.TryParse(Environment.GetEnvironmentVariable("COMPLOCK_SCALE"), out scale) ? scale : Video.DefaultScale;
            if (a[0] == "--remove")
            {
                bool restored;
                bool any = Installer.Remove(pack, citadel, out restored);
                Console.WriteLine("removed: " + any + ", gameinfo.gi restored: " + restored);
                return 0;
            }
            if (a[0] != "--update" && a.Length > 2) pack.Sel = pack.Decode(a[2] == "none" ? "" : a[2]);
            else { Dictionary<string, object> s = Installer.Installed(pack, citadel); if (s != null) pack.Sel = s; }
            Check c = Installer.Look(pack, citadel);
            Console.WriteLine("pack: " + pack.Encode());
            Console.WriteLine("live: " + live + "  installed: " + c.Installed + "  up to date: " + c.UpToDate + "  pending: " + c.Pending + "  gameinfo: " + c.Gameinfo
                + "  gameinfo update: " + c.GameinfoUpdate + "  saved: " + c.Saved);
            foreach (PakPart p in c.Need) Console.WriteLine((c.Updates.Contains(p) ? "update " : "need ") + p.Name + "  " + c.Owner[p].File + (c.Reuse.ContainsKey(p) ? "  (here as " + Path.GetFileName(c.Reuse[p]) + ")" : ""));
            foreach (string n in c.Changed) Console.WriteLine("changed " + n);
            foreach (string n in c.Foreign) Console.WriteLine("foreign " + n + (c.LookAlike.Contains(n) ? "  (looks like the pack's)" : "") + (c.Who.ContainsKey(n) ? "  (" + c.Who[n] + ")" : ""));
            foreach (string n in c.OldCopies) Console.WriteLine("old copy " + n + "  (like " + c.OldOf[n] + ")");
            if (c.PerfPreset != null) Console.WriteLine("performance settings in gameinfo.gi: " + c.PerfPreset);
            foreach (string n in c.Stale) Console.WriteLine("stale " + n);
            foreach (string n in c.Kept) Console.WriteLine("kept " + n);
            Console.WriteLine("video: scale " + pack.RenderScale + "  on " + c.VideoOn + "  at the lowest: " + c.VideoOk + "  video pending: " + c.VideoPending + "  video update: " + c.VideoUpdate);
            Console.WriteLine("download: " + c.DownOf(c.Need));
            if (a[0] == "--look") return 0;
            if (a[0] == "--grimoire")
            {
                List<string> ours = new List<string>();
                foreach (PakFile pf in c.R.Paks) foreach (PakPart p in pf.Parts) if (c.Current.Contains(pf.File) && !c.Kept.Contains(p.Name)) ours.Add(p.Name);
                Console.WriteLine("grimoire records changed: " + Managers.NameInGrimoire(citadel, new Dictionary<string, string>(), ours));
                return 0;
            }
            Plan plan = a[0] == "--install" ? Plan.Full(c) : a[0] == "--apply" ? Plan.Apply(c) : Plan.Update(c, a[2] == "all" ? null : a[2], a[2] == "gameinfo");
            plan.Offline = !live;
            if (Environment.GetEnvironmentVariable("COMPLOCK_REPLACE") == "1")
            {
                foreach (string n in c.LookAlike) plan.Replace.Add(n);
                if (plan.Cleanup) plan.Drop.AddRange(c.OldCopies);
            }
            Installer.Result r = Installer.Install(pack, c, plan, null);
            Console.WriteLine("updated: " + string.Join(", ", r.Updated.ToArray()) + "  gameinfo: " + r.Gameinfo);
            foreach (string n in r.Removed) Console.WriteLine("removed " + n);
            foreach (string n in r.Moved) Console.WriteLine("moved " + n);
            foreach (string n in r.Left) Console.WriteLine("left " + n);
            return 0;
        }
    }
}
