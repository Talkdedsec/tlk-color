using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TlkColor;

static class Ramp
{
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateDC(string driver, string? device, string? output, IntPtr init);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
    [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

    // Tanner Helland's blackbody approximation, 0..255 per channel.
    static (double R, double G, double B) Blackbody(double kelvin)
    {
        double t = kelvin / 100;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
    }

    // Channel gains relative to 6500 K (= no change), scaled so no channel exceeds 1.
    public static double[] Gains(int kelvin)
    {
        var n = Blackbody(6500);
        var v = Blackbody(kelvin);
        double[] g = [v.R / n.R, v.G / n.G, v.B / n.B];
        double max = g.Max();
        return max > 1 ? g.Select(x => x / max).ToArray() : g;
    }

    public static double Curve(Settings s, double gain, int c, double x)
    {
        double v = Math.Pow(x, 100.0 / s.Gamma[c]);
        v = (v - 0.5) * (s.Contrast[c] / 50.0) + 0.5 + (s.Brightness[c] - 50) / 100.0;
        return Math.Clamp(v * gain, 0, 1);
    }

    public static ushort[] Build(Settings s)
    {
        var gains = Gains(s.Kelvin);
        var ramp = new ushort[3 * 256];
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < 256; i++)
                ramp[c * 256 + i] = (ushort)Math.Round(Curve(s, gains[c], c, i / 255.0) * 65535);
        return ramp;
    }

    public enum Result { Ok, Limited, Failed }

    // Windows refuses ramps far from linear unless GdiIcmGammaRange is raised; then apply the strongest
    // blend toward linear that it still accepts instead of silently doing nothing.
    public static Result Apply(string device, ushort[] ramp)
    {
        var dc = CreateDC(device, null, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return Result.Failed;
        try
        {
            if (SetDeviceGammaRamp(dc, ramp)) return Result.Ok;
            bool any = false;
            double lo = 0, hi = 1;
            for (int i = 0; i < 7; i++)
            {
                double t = (lo + hi) / 2;
                if (SetDeviceGammaRamp(dc, Blend(ramp, t))) { lo = t; any = true; }
                else hi = t;
            }
            return any || SetDeviceGammaRamp(dc, Blend(ramp, 0)) ? Result.Limited : Result.Failed;
        }
        finally { DeleteDC(dc); }
    }

    static ushort[] Blend(ushort[] ramp, double t)
    {
        var r = new ushort[ramp.Length];
        for (int i = 0; i < r.Length; i++)
        {
            double linear = (i % 256) * 257.0;
            r[i] = (ushort)Math.Round(linear + (ramp[i] - linear) * t);
        }
        return r;
    }

    public static ushort[]? Read(string device)
    {
        var dc = CreateDC(device, null, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;
        var ramp = new ushort[3 * 256];
        bool ok = GetDeviceGammaRamp(dc, ramp);
        DeleteDC(dc);
        return ok ? ramp : null;
    }
}

// NVIDIA digital vibrance and hue through nvapi64.dll (driver-side, no SDK needed).
static class Nv
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumFn(int index, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NameFn(IntPtr handle, byte[] name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DvcExFn(IntPtr handle, uint output, ref DvcEx info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DvcFn(IntPtr handle, uint output, ref Dvc info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int HueFn(IntPtr handle, uint output, ref HueInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetFn(IntPtr handle, uint output, int value);

    [StructLayout(LayoutKind.Sequential)] struct DvcEx { public uint Version; public int Current, Min, Max, Default; }
    [StructLayout(LayoutKind.Sequential)] struct Dvc { public uint Version; public int Current, Min, Max; }
    [StructLayout(LayoutKind.Sequential)] struct HueInfo { public uint Version; public int Current, Default; }

    static bool loaded;
    static EnumFn? enumDisplays;
    static NameFn? displayName;
    static DvcExFn? getDvcEx, setDvcEx;
    static DvcFn? getDvc;
    static SetFn? setDvc, setHue;
    static HueFn? getHue;
    static readonly Dictionary<string, IntPtr> handles = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> vibrance = new(StringComparer.OrdinalIgnoreCase), hue = new(StringComparer.OrdinalIgnoreCase);

    static T? Fn<T>(uint id) where T : Delegate
    {
        var p = QueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    static uint Ver<T>() where T : struct => (uint)(Marshal.SizeOf<T>() | 1 << 16);

    public static bool HasVibrance(string dev) => vibrance.Contains(dev);
    public static bool HasHue(string dev) => hue.Contains(dev);

    // Display handles are invalidated by display changes, so call again after one.
    public static void Refresh()
    {
        handles.Clear(); vibrance.Clear(); hue.Clear();
        try
        {
            if (!loaded)
            {
                var init = Fn<InitFn>(0x0150E828);
                if (init == null || init() != 0) return;
                enumDisplays = Fn<EnumFn>(0x9ABDD40D);
                displayName = Fn<NameFn>(0x22A78B05);
                getDvcEx = Fn<DvcExFn>(0x0E45002D);
                setDvcEx = Fn<DvcExFn>(0x4A82C2B1);
                getDvc = Fn<DvcFn>(0x4085DE45);
                setDvc = Fn<SetFn>(0x172409B4);
                getHue = Fn<HueFn>(0x95B64341);
                setHue = Fn<SetFn>(0xF5A0F22C);
                loaded = true;
            }
            if (enumDisplays == null || displayName == null) return;
            for (int i = 0; enumDisplays(i, out var h) == 0; i++)
            {
                var buf = new byte[64];
                if (displayName(h, buf) != 0) continue;
                int len = Array.IndexOf(buf, (byte)0);
                var dev = Encoding.ASCII.GetString(buf, 0, len < 0 ? buf.Length : len);
                handles[dev] = h;
                if (Range(h, out _, out _, out _, out _)) vibrance.Add(dev);
                var hi = new HueInfo { Version = Ver<HueInfo>() };
                if (getHue != null && setHue != null && getHue(h, 0, ref hi) == 0) hue.Add(dev);
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    static bool Range(IntPtr h, out int min, out int max, out int def, out bool ex)
    {
        min = max = def = 0; ex = false;
        var e = new DvcEx { Version = Ver<DvcEx>() };
        if (getDvcEx != null && setDvcEx != null && getDvcEx(h, 0, ref e) == 0)
        {
            (min, max, def, ex) = (e.Min, e.Max, e.Default, true);
            return true;
        }
        var b = new Dvc { Version = Ver<Dvc>() };
        if (getDvc != null && setDvc != null && getDvc(h, 0, ref b) == 0)
        {
            (min, max, def) = (b.Min, b.Max, b.Min); // legacy API: minimum is the driver default
            return true;
        }
        return false;
    }

    // percent: 0..100 with 50 = driver default, like the NVIDIA control panel.
    public static bool SetVibrance(string dev, int percent)
    {
        if (!handles.TryGetValue(dev, out var h) || !Range(h, out int min, out int max, out int def, out bool ex)) return false;
        double f = (percent - 50) / 50.0;
        return SetLevel(dev, def + (int)Math.Round(f * (f < 0 ? def - min : max - def)));
    }

    // Raw driver level; used by the self-test to restore whatever the NVIDIA panel had set.
    public static int? Level(string dev)
    {
        if (!handles.TryGetValue(dev, out var h)) return null;
        var e = new DvcEx { Version = Ver<DvcEx>() };
        if (getDvcEx != null && getDvcEx(h, 0, ref e) == 0) return e.Current;
        var b = new Dvc { Version = Ver<Dvc>() };
        return getDvc != null && getDvc(h, 0, ref b) == 0 ? b.Current : null;
    }

    public static bool SetLevel(string dev, int level)
    {
        if (!handles.TryGetValue(dev, out var h) || !Range(h, out int min, out int max, out int def, out bool ex)) return false;
        level = Math.Clamp(level, min, max);
        if (!ex) return setDvc!(h, 0, level) == 0;
        var info = new DvcEx { Version = Ver<DvcEx>(), Current = level, Min = min, Max = max, Default = def };
        return setDvcEx!(h, 0, ref info) == 0;
    }

    public static bool SetHue(string dev, int angle) =>
        handles.TryGetValue(dev, out var h) && setHue != null && setHue(h, 0, angle) == 0;
}

static class Win
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public static void DarkTitle(IntPtr hwnd)
    {
        // 20 = immersive dark mode, 34/35/36 = border/caption/text color (Windows 11, ignored elsewhere)
        foreach (var (attr, value) in new[] { (20, 1), (34, 0x2a2a2a), (35, 0x1a1a1a), (36, 0xe6e6e6) })
        {
            int v = value;
            DwmSetWindowAttribute(hwnd, attr, ref v, 4);
        }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string IcmKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM";

    public static bool Autostart
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("tlk-color") != null; }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("tlk-color", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("tlk-color", false);
        }
    }

    public static bool WideGamma
    {
        get { using var k = Registry.LocalMachine.OpenSubKey(IcmKey); return k?.GetValue("GdiIcmGammaRange") is int v && v >= 256; }
    }

    // Throws Win32Exception if the user cancels the UAC prompt.
    public static void EnableWideGamma()
    {
        using var p = Process.Start(new ProcessStartInfo("reg.exe", $"add \"HKLM\\{IcmKey}\" /v GdiIcmGammaRange /t REG_DWORD /d 256 /f")
        {
            Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden,
        });
        p?.WaitForExit(10000);
    }
}
