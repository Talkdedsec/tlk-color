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

            if (!Nv.HasVibrance(dev)) { Console.WriteLine($"{dev} nvidia: none"); continue; }
            var before = Nv.Level(dev);
            bool vib = Nv.SetVibrance(dev, 80);
            var during = Nv.Level(dev);
            if (before is int lvl) Nv.SetLevel(dev, lvl);
            Console.WriteLine($"{dev} vibrance before={before} set80={vib} during={during} restored={Nv.Level(dev)} hue={Nv.HasHue(dev)}");
        }
    }
}
