using System.Text.Json;

namespace TlkColor;

public class Settings
{
    // Per channel R, G, B. Brightness/contrast 0..100 (50 = neutral), gamma 30..280 (100 = 1.00).
    public int[] Brightness { get; set; } = [50, 50, 50];
    public int[] Contrast { get; set; } = [50, 50, 50];
    public int[] Gamma { get; set; } = [100, 100, 100];
    public int Kelvin { get; set; } = 6500;
    public int Vibrance { get; set; } = 50;
    public int Hue { get; set; }

    public Settings Clone() => new()
    {
        Brightness = [.. Brightness], Contrast = [.. Contrast], Gamma = [.. Gamma],
        Kelvin = Kelvin, Vibrance = Vibrance, Hue = Hue,
    };

    // The config file is user-editable, so never trust its shape.
    public void Fix()
    {
        Brightness = Norm(Brightness, 0, 100, 50);
        Contrast = Norm(Contrast, 0, 100, 50);
        Gamma = Norm(Gamma, 30, 280, 100);
        Kelvin = Math.Clamp(Kelvin, 1900, 10000);
        Vibrance = Math.Clamp(Vibrance, 0, 100);
        Hue = Math.Clamp(Hue, 0, 359);
    }

    static int[] Norm(int[]? a, int min, int max, int def) =>
        a is { Length: 3 } ? a.Select(v => Math.Clamp(v, min, max)).ToArray() : [def, def, def];
}

public class Profile
{
    public string Name { get; set; } = "";
    public Settings Settings { get; set; } = new();
}

public class Config
{
    public Dictionary<string, Settings> Monitors { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public string Lang { get; set; } = "";

    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tlk-color");
    static readonly string FilePath = Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        Config c;
        try { c = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { c = new(); }
        c.Monitors = (c.Monitors ?? new()).Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        c.Profiles = (c.Profiles ?? new()).Where(p => p?.Settings != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
        foreach (var s in c.Monitors.Values.Concat(c.Profiles.Select(p => p.Settings))) s.Fix();
        if (c.Lang is not ("tr" or "en")) c.Lang = L.Default;
        return c;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp"; // write-then-move so a crash never leaves a half-written config
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true);
    }

    public Settings Get(string dev) => Monitors.TryGetValue(dev, out var s) ? s : new();
    public Settings For(string dev) => Monitors.TryGetValue(dev, out var s) ? s : Monitors[dev] = new();
}
