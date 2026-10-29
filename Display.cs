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

    static DEVMODE? For(string dev, Mode mode, int orientation)
    {
        if (Current(dev) is not { } dm) return null;
        bool portrait = orientation % 2 == 1;
        dm.dmPelsWidth = portrait ? mode.Height : mode.Width;
        dm.dmPelsHeight = portrait ? mode.Width : mode.Height;
        dm.dmDisplayFrequency = mode.Hz;
        dm.dmDisplayOrientation = orientation;
        dm.dmFields = DmWidth | DmHeight | DmFrequency | DmOrientation;
        return dm;
    }

    // Asks the driver without touching the screen; 0 = it would accept the mode.
    public static int Test(string dev, Mode mode, int orientation)
    {
        if (For(dev, mode, orientation) is not { } dm) return -1;
        return ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CdsTest, IntPtr.Zero);
    }

    // 0 = success. Tests first so a bad mode never reaches the screen.
    public static int Apply(string dev, Mode mode, int orientation)
    {
        if (For(dev, mode, orientation) is not { } dm) return -1;
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

    [DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPaths, byte[] paths, uint numModes, byte[] modes, uint flags);

    // DISPLAYCONFIG_PATH_INFO is 72 bytes: source (LUID, id, ...) then target; target.scaling sits at offset 44.
    const int PathSize = 72, ModeSize = 64, ScalingOffset = 44;

    record Topology(byte[] Paths, uint PathCount, byte[] Modes, uint ModeCount);

    // Virtual-mode-aware (Win10+) so scaling round-trips; without it Windows resets scaling to identity on apply.
    const uint QdcOnlyActive = 2, QdcVirtualModeAware = 0x10;

    static Topology? Query()
    {
        const uint flags = QdcOnlyActive | QdcVirtualModeAware;
        if (GetDisplayConfigBufferSizes(flags, out uint np, out uint nm) != 0) return null;
        var paths = new byte[np * PathSize];
        var modes = new byte[nm * ModeSize];
        return QueryDisplayConfig(flags, ref np, paths, ref nm, modes, IntPtr.Zero) == 0 ? new Topology(paths, np, modes, nm) : null;
    }

    static (uint Low, int High, uint Id) SourceAt(byte[] paths, int i) =>
        (BitConverter.ToUInt32(paths, i * PathSize), BitConverter.ToInt32(paths, i * PathSize + 4), BitConverter.ToUInt32(paths, i * PathSize + 8));

    // Maps a GDI name (\\.\DISPLAY1) to its active path index.
    static int PathOf(Topology t, string dev)
    {
        for (int i = 0; i < t.PathCount; i++)
        {
            var name = Packet(1, 84, SourceAt(t.Paths, i)); // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
            if (DisplayConfigGetDeviceInfo(name) != 0) continue;
            var gdi = System.Text.Encoding.Unicode.GetString(name, 20, 64).TrimEnd('\0');
            if (gdi.Equals(dev, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    static (uint Low, int High, uint Id)? Source(string dev) =>
        Query() is { } t && PathOf(t, dev) is var i && i >= 0 ? SourceAt(t.Paths, i) : null;

    // DISPLAYCONFIG_SCALING: 1 identity, 2 centered, 3 stretched, 4 aspect-ratio (black bars), 128 preferred.
    public static int Scaling(string dev) =>
        Query() is { } t && PathOf(t, dev) is var i && i >= 0 ? BitConverter.ToInt32(t.Paths, i * PathSize + ScalingOffset) : 0;

    // True when the desktop size differs from the signal sent to the panel, i.e. a fit mode actually applies.
    // Virtual-mode-aware paths pack the mode index in the high 16 bits of source/target modeInfoIdx.
    public static bool IsScaled(string dev)
    {
        if (Query() is not { } t || PathOf(t, dev) is var i && i < 0) return false;
        int o = i * PathSize;
        uint src = BitConverter.ToUInt32(t.Paths, o + 12) >> 16, tgt = BitConverter.ToUInt32(t.Paths, o + 32) >> 16;
        if (src >= t.ModeCount || tgt >= t.ModeCount) return false;
        int sw = BitConverter.ToInt32(t.Modes, (int)src * ModeSize + 16), sh = BitConverter.ToInt32(t.Modes, (int)src * ModeSize + 20);
        int tw = BitConverter.ToInt32(t.Modes, (int)tgt * ModeSize + 40), th = BitConverter.ToInt32(t.Modes, (int)tgt * ModeSize + 44);
        return (Math.Max(sw, sh), Math.Min(sw, sh)) != (Math.Max(tw, th), Math.Min(tw, th));
    }

    public static bool SetScaling(string dev, int scaling)
    {
        if (Query() is not { } t || PathOf(t, dev) is var i && i < 0) return false;
        BitConverter.GetBytes(scaling).CopyTo(t.Paths, i * PathSize + ScalingOffset);
        const uint Apply = 0x80, UseSupplied = 0x20, Save = 0x200, AllowChanges = 0x400, VirtualModeAware = 0x8000;
        return SetDisplayConfig(t.PathCount, t.Paths, t.ModeCount, t.Modes, Apply | UseSupplied | Save | AllowChanges | VirtualModeAware) == 0;
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
