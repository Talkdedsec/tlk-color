using System.Runtime.InteropServices;

namespace TlkColor;

// Resolution, refresh rate, orientation (ChangeDisplaySettingsEx) and per-monitor scale (DisplayConfig DPI packets).
static class Display
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsEx(string device, int mode, ref DEVMODE dm, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, byte[] paths, ref uint numModes, byte[] modes, IntPtr topology);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(byte[] packet);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(byte[] packet);

    const int CurrentSettings = -1, CdsUpdateRegistry = 1, CdsTest = 2;
    const int DmOrientation = 0x80, DmWidth = 0x80000, DmHeight = 0x100000, DmFrequency = 0x400000;

    public record Mode(int Width, int Height, int Hz);

    static DEVMODE New() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };

    public static DEVMODE? Current(string dev)
    {
        var dm = New();
        return EnumDisplaySettingsEx(dev, CurrentSettings, ref dm, 0) ? dm : null;
    }

    // Modes in landscape terms, largest first.
    // shortcut: assumes landscape-native panels (w >= h); portrait-native tablets would list swapped sizes.
    public static List<Mode> Modes(string dev)
    {
        var set = new HashSet<Mode>();
        var dm = New();
        for (int i = 0; EnumDisplaySettingsEx(dev, i, ref dm, 0); i++)
            if (dm.dmBitsPerPel >= 24 && dm.dmDisplayFrequency > 1)
                set.Add(new Mode(Math.Max(dm.dmPelsWidth, dm.dmPelsHeight), Math.Min(dm.dmPelsWidth, dm.dmPelsHeight), dm.dmDisplayFrequency));
        return set.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Hz).ToList();
    }

    public static Mode? CurrentMode(string dev) => Current(dev) is { } d
        ? new Mode(Math.Max(d.dmPelsWidth, d.dmPelsHeight), Math.Min(d.dmPelsWidth, d.dmPelsHeight), d.dmDisplayFrequency)
        : null;

    public static int Orientation(string dev) => Current(dev)?.dmDisplayOrientation ?? 0;

    // 0 = success. Tests first so a bad mode never reaches the screen.
    public static int Apply(string dev, Mode mode, int orientation)
    {
        if (Current(dev) is not { } dm) return -1;
        bool portrait = orientation % 2 == 1;
        dm.dmPelsWidth = portrait ? mode.Height : mode.Width;
        dm.dmPelsHeight = portrait ? mode.Width : mode.Height;
        dm.dmDisplayFrequency = mode.Hz;
        dm.dmDisplayOrientation = orientation;
        dm.dmFields = DmWidth | DmHeight | DmFrequency | DmOrientation;
        int test = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CdsTest, IntPtr.Zero);
        return test != 0 ? test : ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    }

    public static int Restore(string dev, DEVMODE previous)
    {
        previous.dmFields = DmWidth | DmHeight | DmFrequency | DmOrientation;
        return ChangeDisplaySettingsEx(dev, ref previous, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    }

    // Windows' scale steps; the DPI packets address them relative to the monitor's recommended step.
    static readonly int[] ScaleSteps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    public record ScaleInfo(int Current, int Recommended, int[] Allowed);

    static byte[] Packet(int type, int size, (uint Low, int High, uint Id) src)
    {
        var p = new byte[size];
        BitConverter.GetBytes(type).CopyTo(p, 0);
        BitConverter.GetBytes(size).CopyTo(p, 4);
        BitConverter.GetBytes(src.Low).CopyTo(p, 8);
        BitConverter.GetBytes(src.High).CopyTo(p, 12);
        BitConverter.GetBytes(src.Id).CopyTo(p, 16);
        return p;
    }

    // Maps a GDI name (\\.\DISPLAY1) to its DisplayConfig source (adapter LUID + source id).
    static (uint Low, int High, uint Id)? Source(string dev)
    {
        const uint OnlyActive = 2;
        const int PathSize = 72, ModeSize = 64;
        if (GetDisplayConfigBufferSizes(OnlyActive, out uint np, out uint nm) != 0) return null;
        var paths = new byte[np * PathSize];
        var modes = new byte[nm * ModeSize];
        if (QueryDisplayConfig(OnlyActive, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return null;
        for (int i = 0; i < np; i++)
        {
            int o = i * PathSize;
            var src = (BitConverter.ToUInt32(paths, o), BitConverter.ToInt32(paths, o + 4), BitConverter.ToUInt32(paths, o + 8));
            var name = Packet(1, 84, src); // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
            if (DisplayConfigGetDeviceInfo(name) != 0) continue;
            var gdi = System.Text.Encoding.Unicode.GetString(name, 20, 64).TrimEnd('\0');
            if (gdi.Equals(dev, StringComparison.OrdinalIgnoreCase)) return src;
        }
        return null;
    }

    public static ScaleInfo? Scale(string dev)
    {
        if (Source(dev) is not { } src) return null;
        var p = Packet(-3, 32, src); // undocumented GET_DPI_SCALE, used by Settings app
        if (DisplayConfigGetDeviceInfo(p) != 0) return null;
        int min = BitConverter.ToInt32(p, 20), cur = BitConverter.ToInt32(p, 24), max = BitConverter.ToInt32(p, 28);
        int rec = Math.Abs(min), last = Math.Min(rec + max, ScaleSteps.Length - 1);
        if (rec >= ScaleSteps.Length) return null;
        return new ScaleInfo(ScaleSteps[Math.Clamp(rec + cur, 0, last)], ScaleSteps[rec], ScaleSteps[..(last + 1)]);
    }

    public static bool SetScale(string dev, int percent)
    {
        if (Source(dev) is not { } src || Scale(dev) is not { } info) return false;
        int idx = Array.IndexOf(ScaleSteps, percent), rec = Array.IndexOf(ScaleSteps, info.Recommended);
        if (idx < 0 || !info.Allowed.Contains(percent)) return false;
        var p = Packet(-4, 24, src); // undocumented SET_DPI_SCALE
        BitConverter.GetBytes(idx - rec).CopyTo(p, 20);
        return DisplayConfigSetDeviceInfo(p) == 0;
    }
}
