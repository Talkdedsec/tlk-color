using System.Diagnostics;

namespace TlkColor;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var id = Ramp.Build(new Settings()); // defaults must give a linear ramp
        Debug.Assert(id[0] == 0 && id[255] == 65535 && id[256 + 128] == (ushort)Math.Round(128 / 255.0 * 65535));
        Debug.Assert(Ramp.Gains(6500).All(g => Math.Abs(g - 1) < 1e-9) && Ramp.Gains(1900)[2] < Ramp.Gains(1900)[0]);

        if (args.Contains("--selftest")) { SelfTest(); return; }
        if (args.Contains("--selftest-display")) { DisplayTest(); return; }

        using var mutex = new Mutex(true, "tlk-color-instance", out bool first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting("tlk-color-show").Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return;
        }
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, "tlk-color-show");

        ApplicationConfiguration.Initialize();
        var form = new MainForm(args.Contains("--tray"));
        ThreadPool.RegisterWaitForSingleObject(show, (_, _) => { if (form.IsHandleCreated) form.BeginInvoke(form.ShowMain); }, null, -1, false);
        Application.Run(form);
    }

    // Briefly switches a secondary monitor's refresh rate and scale, reads them back, then restores both.
    static void DisplayTest()
    {
        var sc = Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen!;
        var dev = sc.DeviceName;
        if (Display.Current(dev) is not { } prev || Display.CurrentMode(dev) is not { } cur) { Console.WriteLine("no mode"); return; }
        var alt = Display.Modes(dev).FirstOrDefault(m => m.Width == cur.Width && m.Height == cur.Height && m.Hz != cur.Hz);
        try
        {
            if (alt != null)
            {
                int r = Display.Apply(dev, alt, Display.Orientation(dev));
                Console.WriteLine($"{dev} apply {alt.Hz}Hz -> code={r} now={Display.CurrentMode(dev)}");
            }
        }
        finally { Console.WriteLine($"{dev} restore code={Display.Restore(dev, prev)} now={Display.CurrentMode(dev)}"); }

        foreach (var d in Screen.AllScreens.Select(x => x.DeviceName)) ScalingTest(d);

        Nv.Refresh();
        var nv = Screen.AllScreens.Select(x => x.DeviceName).FirstOrDefault(Nv.CanCustom);
        if (nv != null)
        {
            int r = Nv.TryCustom(nv, 1776, 1000, 60);
            try { Console.WriteLine($"{nv} custom 1776x1000 try={r} now={Display.CurrentMode(nv)}"); Thread.Sleep(1500); }
            finally { if (r == 0) Console.WriteLine($"{nv} custom revert ok={Nv.RevertCustom(nv)} now={Display.CurrentMode(nv)}"); }
        }

        if (Display.Scale(dev) is not { } s) { Console.WriteLine("scale n/a"); return; }
        int other = s.Allowed.First(v => v != s.Current);
        try { Console.WriteLine($"{dev} scale {s.Current}->{other} ok={Display.SetScale(dev, other)} now={Display.Scale(dev)?.Current}"); }
        finally { Console.WriteLine($"{dev} scale restore ok={Display.SetScale(dev, s.Current)} now={Display.Scale(dev)?.Current}"); }
    }

    // Scaling only matters on a non-native mode, so switch to 4:3, try each mode, then restore.
    static void ScalingTest(string dev)
    {
        int scaling = Display.Scaling(dev);
        var four3 = Display.Modes(dev).FirstOrDefault(m => m.Width * 3 == m.Height * 4 && m.Width >= 1024);
        if (four3 == null || Display.Current(dev) is not { } before43) return;
        try
        {
            Console.WriteLine($"{dev} native scaled={Display.IsScaled(dev)}");
            Console.WriteLine($"{dev} 4:3 {four3} code={Display.Apply(dev, four3, 0)} scaling={Display.Scaling(dev)} scaled={Display.IsScaled(dev)}");
            foreach (var v in new[] { 3, 4, 2 })
                Console.WriteLine($"{dev} scaling ->{v} ok={Display.SetScaling(dev, v)} now={Display.Scaling(dev)}");
            Thread.Sleep(800);
        }
        finally
        {
            Display.SetScaling(dev, scaling);
            Console.WriteLine($"{dev} 4:3 restore code={Display.Restore(dev, before43)} now={Display.CurrentMode(dev)} scaling={Display.Scaling(dev)}");
        }
    }

    // Applies test values on the real hardware, reads them back, then restores linear ramps and the original vibrance.
    static void SelfTest()
    {
        Nv.Refresh();
        foreach (var sc in Screen.AllScreens)
        {
            var dev = sc.DeviceName;
            var ramp = Ramp.Build(new Settings { Gamma = [150, 150, 150], Kelvin = 5000 });
            var set = Ramp.Apply(dev, ramp);
            var back = Ramp.Read(dev);
            int diff = back == null ? -1 : ramp.Zip(back, (a, b) => Math.Abs(a - b)).Max();
            var warm = Ramp.Apply(dev, Ramp.Build(new Settings { Kelvin = 1900 }));
            var warmBack = Ramp.Read(dev);
            Console.WriteLine($"{dev} 1900K blue[255] applied={warmBack?[3 * 256 - 1]} (linear=65535)");
            Ramp.Apply(dev, Ramp.Build(new Settings()));
            Console.WriteLine($"{dev} gamma set={set} readback maxdiff={diff} 1900K={warm}");

            // Display mode and scale are read-only here; changing them needs the user's confirmation.
            var modes = Display.Modes(dev);
            var scale = Display.Scale(dev);
            Console.WriteLine($"{dev} mode={Display.CurrentMode(dev)} orient={Display.Orientation(dev)} modes={modes.Count} top={string.Join(" ", modes.Take(3))}");
            Console.WriteLine($"{dev} scale={(scale == null ? "n/a" : $"{scale.Current}% rec={scale.Recommended}% allowed={string.Join(",", scale.Allowed)}")} bounds={sc.Bounds}");
            Console.WriteLine($"{dev} 4:3 listed={string.Join(" ", modes.Where(m => m.Width * 3 == m.Height * 4).Select(m => $"{m.Width}x{m.Height}@{m.Hz}"))}");
            Console.WriteLine($"{dev} scaling={Display.Scaling(dev)} custom={Nv.CanCustom(dev)} layoutOk={Nv.LayoutOk} timing1776x1000@60: {Nv.ProbeTiming(dev, 1776, 1000, 60)}");

            if (!Nv.HasVibrance(dev)) { Console.WriteLine($"{dev} nvidia: none"); continue; }
            Console.WriteLine($"{dev} vibrance range {Nv.Describe(dev)}");
            var before = Nv.Level(dev);
            bool vib = Nv.SetVibrance(dev, 80);
            var during = Nv.Level(dev);
            if (before is int lvl) Nv.SetLevel(dev, lvl);
            Console.WriteLine($"{dev} vibrance before={before} set80={vib} during={during} restored={Nv.Level(dev)} hue={Nv.HasHue(dev)}");
        }
    }
}
