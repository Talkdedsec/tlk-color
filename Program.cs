using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TlkColor;

static class Program
{
    [STAThread]
    static void Main()
    {
        var id = Gamma.Build(0, 1, 1, 1, 1, 1); // defaults must give a linear ramp
        System.Diagnostics.Debug.Assert(id[0] == 0 && id[255] == 65535 && id[256 + 128] == 128 * 65535 / 255);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

static class Gamma
{
    [DllImport("gdi32.dll")] static extern IntPtr CreateDC(string driver, string device, string? output, IntPtr init);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

    // brightness -1..1, contrast 0..2, gamma 0.3..3; channel gains 0..1 (color temperature-ish)
    public static ushort[] Build(double brightness, double contrast, double gamma, double r, double g, double b)
    {
        var ramp = new ushort[3 * 256];
        double[] gains = [r, g, b];
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < 256; i++)
            {
                double v = Math.Pow(i / 255.0, 1.0 / gamma);
                v = ((v - 0.5) * contrast + 0.5 + brightness) * gains[c];
                ramp[c * 256 + i] = (ushort)(Math.Clamp(v, 0, 1) * 65535);
            }
        return ramp;
    }

    // Returns false if any monitor rejected the ramp (Windows refuses ramps that deviate too far from linear).
    public static bool Apply(ushort[] ramp)
    {
        bool ok = true;
        foreach (var s in Screen.AllScreens)
        {
            var dc = CreateDC(s.DeviceName, s.DeviceName, null, IntPtr.Zero);
            ok &= dc != IntPtr.Zero && SetDeviceGammaRamp(dc, ramp);
            DeleteDC(dc);
        }
        return ok;
    }
}

class Slider : Control
{
    public required string Label;
    public int Min, Max, Default, Value;
    public event Action? ValueChanged;

    public Slider()
    {
        DoubleBuffered = true;
        Height = 26;
        Cursor = Cursors.Hand;
    }

    public void Set(int v)
    {
        v = Math.Clamp(v, Min, Max);
        if (v == Value) return;
        Value = v;
        Invalidate();
        ValueChanged?.Invoke();
    }

    void SetFromX(int x) => Set(Min + (int)Math.Round((Max - Min) * Math.Clamp(x / (double)Width, 0, 1)));

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetFromX(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetFromX(e.X); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) => Set(Default);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(0x2a, 0x2a, 0x2a));
        int fill = (int)(Width * (Value - Min) / (double)(Max - Min));
        using (var b = new SolidBrush(Color.FromArgb(0x55, 0x4a, 0x10))) g.FillRectangle(b, 0, 0, fill, Height);
        using (var b = new SolidBrush(Color.FromArgb(0xFF, 0xD6, 0x00))) g.FillRectangle(b, Math.Max(fill - 3, 0), 0, 3, Height);
        var flags = TextFormatFlags.VerticalCenter;
        TextRenderer.DrawText(g, Label, Font, new Rectangle(8, 0, Width - 16, Height), Color.Gainsboro, flags | TextFormatFlags.Left);
        TextRenderer.DrawText(g, Value.ToString(), Font, new Rectangle(8, 0, Width - 16, Height), Color.White, flags | TextFormatFlags.Right);
    }
}

class MainForm : Form
{
    readonly Slider bright, contrast, gamma, red, green, blue;
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        Text = "tlk-color";
        BackColor = Color.FromArgb(0x1a, 0x1a, 0x1a);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(320, 270);

        bright = Add("Parlaklık", -50, 50, 0);
        contrast = Add("Kontrast", 50, 150, 100);
        gamma = Add("Gama", 50, 250, 100);
        red = Add("Kırmızı", 50, 100, 100);
        green = Add("Yeşil", 50, 100, 100);
        blue = Add("Mavi", 50, 100, 100);

        var reset = new Button
        {
            Text = "Sıfırla", Location = new Point(12, 12 + 6 * 34), Size = new Size(ClientSize.Width - 24, 30), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2a, 0x2a, 0x2a), ForeColor = Color.FromArgb(0xFF, 0xD6, 0x00),
        };
        reset.FlatAppearance.BorderColor = Color.FromArgb(0xFF, 0xD6, 0x00);
        reset.Click += (_, _) => { foreach (var s in Controls.OfType<Slider>()) s.Set(s.Default); };
        Controls.Add(reset);
        Controls.Add(status);
        status.Text = "Çift tık: değeri sıfırla";
    }

    Slider Add(string label, int min, int max, int def)
    {
        int y = 12 + Controls.OfType<Slider>().Count() * 34;
        var s = new Slider { Label = label, Min = min, Max = max, Default = def, Value = def, Location = new Point(12, y), Width = ClientSize.Width - 24 };
        s.ValueChanged += ApplyAll;
        Controls.Add(s);
        return s;
    }

    void ApplyAll()
    {
        var ramp = Gamma.Build(bright.Value / 100.0, contrast.Value / 100.0, gamma.Value / 100.0,
                               red.Value / 100.0, green.Value / 100.0, blue.Value / 100.0);
        status.Text = Gamma.Apply(ramp) ? "Uygulandı" : "Windows bu değeri reddetti (çok uç)";
    }
}
