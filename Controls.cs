using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TlkColor;

static class Theme
{
    public static float Scale = 1;
    public static int P(float v) => (int)Math.Round(v * Scale);

    public static readonly Color
        Bg = Gray(0x17), Side = Gray(0x11), Surface = Gray(0x1d), Ctl = Gray(0x25), Hover = Gray(0x2e), Line = Gray(0x29),
        Text = Gray(0xf2), Text2 = Gray(0xb4), Dim = Gray(0x80), Faint = Gray(0x58),
        Accent = Color.FromArgb(0xFF, 0xD6, 0x00), Warn = Color.FromArgb(0xff, 0x9f, 0x43), Danger = Color.FromArgb(0xff, 0x6b, 0x5c);

    // Index 0 = all channels, 1..3 = R, G, B.
    public static readonly Color[] Channel =
        [Accent, Color.FromArgb(0xff, 0x5c, 0x5c), Color.FromArgb(0x4c, 0xd9, 0x64), Color.FromArgb(0x4d, 0x9d, 0xff)];

    // Honors the Windows "animation effects" accessibility switch.
    public static readonly bool Motion = SystemInformation.UIEffectsEnabled;

    static Color Gray(int v) => Color.FromArgb(v, v, v);

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    static readonly HashSet<string> fonts = new InstalledFontCollection().Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    static string Pick(params string[] names) => names.FirstOrDefault(fonts.Contains) ?? "Segoe UI";
    static readonly string body = Pick("Segoe UI Variable Text", "Segoe UI"),
        bodySemi = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold"),
        displayLight = Pick("Segoe UI Variable Display Light", "Segoe UI Light"),
        displaySemi = Pick("Segoe UI Variable Display Semib", "Segoe UI Semibold"),
        mono = Pick("Cascadia Mono", "Consolas");

    public static Font Body(float pt) => new(body, pt);
    public static Font BodySemi(float pt) => new(bodySemi, pt);
    public static Font DisplayLight(float pt) => new(displayLight, pt);
    public static Font DisplaySemi(float pt) => new(displaySemi, pt);
    public static Font Mono(float pt) => new(mono, pt);
    static Font? caption;
    public static Font Caption => caption ??= BodySemi(7.5f);

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius);
        g.FillPath(b, p);
    }

    public static void Stroke(Graphics g, Color c, RectangleF r, float radius, float width = 1)
    {
        using var pen = new Pen(c, width);
        using var p = Round(RectangleF.Inflate(r, -width / 2, -width / 2), radius);
        g.DrawPath(pen, p);
    }

    public static void Dot(Graphics g, Color c, float cx, float cy, float r) => Fill(g, c, new RectangleF(cx - r, cy - r, 2 * r, 2 * r), r);

    // Letter-spaced uppercase label; GDI has no tracking, so draw glyph by glyph.
    public static float Tracked(Graphics g, string text, Font font, Color color, float x, float y, float tracking)
    {
        const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        foreach (char ch in text)
        {
            var s = ch.ToString();
            TextRenderer.DrawText(g, s, font, new Point((int)x, (int)y), color, f);
            x += (ch == ' ' ? font.Size * Scale * 0.45f : TextRenderer.MeasureText(g, s, font, Size.Empty, f).Width) + tracking;
        }
        return x;
    }

    public static Icon MakeIcon(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawMark(g, new RectangleF(0.5f, 0.5f, size - 1, size - 1));
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public static void DrawMark(Graphics g, RectangleF r)
    {
        for (int i = 0; i < 3; i++)
        {
            using var b = new SolidBrush(Channel[i + 1]);
            g.FillPie(b, r.X, r.Y, r.Width, r.Height, -90 + i * 120, 120);
        }
        float c = r.Width * 0.38f;
        using var a = new SolidBrush(Accent);
        g.FillEllipse(a, r.X + (r.Width - c) / 2, r.Y + (r.Height - c) / 2, c, c);
    }

    public static ContextMenuStrip Menu() => new()
    {
        Renderer = new DarkRenderer(), BackColor = Ctl, ForeColor = Text, Font = Body(9.5f), Padding = new Padding(0, P(4), 0, P(4)),
    };

    public static ToolTip Tip()
    {
        var t = new ToolTip { OwnerDraw = true, InitialDelay = 500, ReshowDelay = 100 };
        var font = Body(9f);
        t.Popup += (_, e) => e.ToolTipSize = TextRenderer.MeasureText(t.GetToolTip(e.AssociatedControl), font) + new Size(P(20), P(12));
        t.Draw += (_, e) =>
        {
            e.Graphics.Clear(Ctl);
            using var pen = new Pen(Hover);
            e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, e.Bounds, Text2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        return t;
    }
}

// Frame-rate independent exponential easing (feels like power-out); one shared timer drives every animation.
sealed class Anim
{
    static readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    static readonly List<Anim> running = new();
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static double last;

    readonly Control owner;
    readonly double speed;
    public double Value, Target;

    static Anim() => timer.Tick += (_, _) => Tick();

    public Anim(Control owner, double value = 0, double speed = 16)
    {
        this.owner = owner;
        this.speed = speed;
        Value = Target = value;
    }

    public void To(double target, bool instant = false)
    {
        Target = target;
        if (instant || !Theme.Motion) { Value = target; running.Remove(this); owner.Invalidate(); return; }
        if (!running.Contains(this)) running.Add(this);
        if (!timer.Enabled) { last = clock.Elapsed.TotalSeconds; timer.Start(); }
    }

    static void Tick()
    {
        double now = clock.Elapsed.TotalSeconds, dt = Math.Min(now - last, 0.05);
        last = now;
        for (int i = running.Count - 1; i >= 0; i--)
        {
            var a = running[i];
            a.Value += (a.Target - a.Value) * (1 - Math.Exp(-a.speed * dt));
            if (Math.Abs(a.Target - a.Value) < 0.0015) { a.Value = a.Target; running.RemoveAt(i); }
            if (!a.owner.IsDisposed) a.owner.Invalidate();
        }
        if (running.Count == 0) timer.Stop();
    }
}

class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Faint : e.Item.Selected ? Theme.Text : Theme.Text2;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Dot(e.Graphics, Theme.Accent, r.X + r.Width / 2f, r.Y + r.Height / 2f, Theme.P(3.5f));
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Dim;
        base.OnRenderArrow(e);
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Ctl;
        public override Color MenuBorder => Theme.Hover;
        public override Color MenuItemBorder => Theme.Hover;
        public override Color MenuItemSelected => Theme.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Hover;
        public override Color MenuItemPressedGradientBegin => Theme.Hover;
        public override Color MenuItemPressedGradientEnd => Theme.Hover;
        public override Color ImageMarginGradientBegin => Theme.Ctl;
        public override Color ImageMarginGradientMiddle => Theme.Ctl;
        public override Color ImageMarginGradientEnd => Theme.Ctl;
        public override Color SeparatorDark => Theme.Hover;
        public override Color SeparatorLight => Theme.Ctl;
        public override Color CheckBackground => Theme.Ctl;
        public override Color CheckSelectedBackground => Theme.Hover;
        public override Color CheckPressedBackground => Theme.Hover;
    }
}

abstract class DarkControl : Control
{
    protected readonly Anim hoverA;

    protected DarkControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        hoverA = new Anim(this, 0, 20);
        Font = Theme.Body(9.5f);
    }

    protected override void OnMouseEnter(EventArgs e) { hoverA.To(1); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hoverA.To(0); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected Graphics Begin(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        return g;
    }

    protected const TextFormatFlags AlignLeft = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
    protected const TextFormatFlags AlignRight = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
    protected const TextFormatFlags AlignCenter = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
}

// Tracked uppercase section label, optionally followed by a hairline rule.
class Caption : DarkControl
{
    public bool Rule;

    public Caption(string text, bool rule = false)
    {
        Text = text;
        Rule = rule;
        Font = Theme.Caption;
        Height = Theme.P(18);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float end = Theme.Tracked(g, Text, Font, Theme.Dim, 0, 0, Theme.P(1.6f));
        if (!Rule) return;
        using var pen = new Pen(Theme.Line);
        float y = Theme.P(7);
        g.DrawLine(pen, end + Theme.P(10), y, Width, y);
    }
}

class Slider : DarkControl
{
    public string Label = "", Unit = "", Note = "";
    public int Min, Max = 100, Default, Step = 1;
    public Func<int, string> Format = v => v.ToString();
    public Color Accent = Theme.Accent;
    public Color[]? Gradient; // when set, the track itself shows what the slider does
    public event Action? ValueChanged;
    readonly Anim posA, dragA;
    int value;
    bool drag;

    public int Value => value;
    public string ValueText => Format(value);

    public Slider()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Theme.P(58);
        posA = new Anim(this, 0, 18);
        dragA = new Anim(this, 0, 22);
    }

    double Frac(int v) => (v - Min) / (double)(Max - Min);

    public void SetSilent(int v, bool animate = true)
    {
        value = Snap(v);
        posA.To(Frac(value), !animate);
    }

    public void Set(int v, bool animate = true)
    {
        v = Snap(v);
        posA.To(Frac(v), !animate);
        if (v == value) return;
        value = v;
        Invalidate();
        ValueChanged?.Invoke();
    }

    int Snap(int v) => Math.Clamp(Min + (int)Math.Round((v - Min) / (double)Step) * Step, Min, Max);
    int Pad => Theme.P(9);
    float TrackY => Height - Theme.P(17);
    float XOf(double frac) => Pad + (Width - 2 * Pad) * (float)frac;
    void SetFromX(int x) => Set(Min + (int)Math.Round((Max - Min) * Math.Clamp((x - Pad) / (double)(Width - 2 * Pad), 0, 1)), false);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left) { drag = true; dragA.To(1); SetFromX(e.X); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; dragA.To(0); base.OnMouseUp(e); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) => Set(Default);
    protected override void OnMouseWheel(MouseEventArgs e) => Set(value + Math.Sign(e.Delta) * Step);
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Set(value - Step); break;
            case Keys.Right or Keys.Up: Set(value + Step); break;
            case Keys.PageDown: Set(value - 10 * Step); break;
            case Keys.PageUp: Set(value + 10 * Step); break;
            case Keys.Home: Set(Min); break;
            case Keys.End: Set(Max); break;
            case Keys.Delete or Keys.Back: Set(Default); break;
        }
        base.OnKeyDown(e);
    }

    static Color Sample(Color[] stops, double t)
    {
        double x = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int i = Math.Min((int)x, stops.Length - 2);
        return Theme.Mix(stops[i], stops[i + 1], x - i);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        bool on = Enabled;
        var head = new Rectangle(Pad, 0, Width - 2 * Pad, Theme.P(26));
        TextRenderer.DrawText(g, Label, Font, head, on ? Theme.Mix(Theme.Text2, Theme.Text, hoverA.Value) : Theme.Faint, AlignLeft);
        if (!on && Note != "")
            TextRenderer.DrawText(g, Note, Font, head, Theme.Faint, AlignRight);
        else
        {
            using var mono = Theme.Mono(9.5f);
            TextRenderer.DrawText(g, ValueText + Unit, mono, head, !on ? Theme.Faint : value != Default ? Theme.Text : Theme.Dim, AlignRight);
        }

        float h = Theme.P(6), y = TrackY, left = Pad, right = Width - Pad;
        var track = new RectangleF(left, y - h / 2, right - left, h);
        double frac = posA.Value;
        float x = XOf(frac), x0 = XOf(Frac(Default));
        if (Gradient != null)
        {
            using var path = Theme.Round(track, h / 2);
            using var br = new LinearGradientBrush(new RectangleF(left - 1, y - h, right - left + 2, 2 * h), Color.Black, Color.Black, 0f)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = on ? Gradient : Gradient.Select(c => Theme.Mix(Theme.Ctl, c, 0.25)).ToArray(),
                    Positions = Enumerable.Range(0, Gradient.Length).Select(i => i / (float)(Gradient.Length - 1)).ToArray(),
                },
            };
            g.FillPath(br, path);
        }
        else
        {
            Theme.Fill(g, Theme.Ctl, track, h / 2);
            // Fill from the neutral point so it reads as "distance from default".
            Theme.Fill(g, on ? Accent : Theme.Hover, RectangleF.FromLTRB(Math.Min(x0, x), y - h / 2, Math.Max(x0, x), y + h / 2), h / 2);
        }
        Theme.Fill(g, Theme.Mix(Theme.Faint, Theme.Text2, hoverA.Value), new RectangleF(x0 - Theme.P(1), y - h / 2 - Theme.P(6), Theme.P(2), Theme.P(4)), 1);

        float r = Theme.P(7) + (float)(hoverA.Value * Theme.P(1.5f) + dragA.Value * Theme.P(1.5f));
        Theme.Dot(g, Color.FromArgb(90, 0, 0, 0), x, y + Theme.P(1), r + Theme.P(2.5f)); // soft shadow
        Theme.Dot(g, on ? Theme.Text : Theme.Dim, x, y, r);
        if (Gradient != null) Theme.Dot(g, on ? Sample(Gradient, frac) : Theme.Hover, x, y, r - Theme.P(3));
        else if (on) Theme.Dot(g, Theme.Mix(Theme.Text, Accent, dragA.Value), x, y, r - Theme.P(3));
        if (Focused && on)
        {
            using var ring = new Pen(Color.FromArgb(150, Accent), Theme.P(1.5f));
            float rr = r + Theme.P(4);
            g.DrawEllipse(ring, x - rr, y - rr, 2 * rr, 2 * rr);
        }
    }
}

class Segmented : DarkControl
{
    public string[] Items = [];
    public event Action? SelectedChanged;
    readonly Anim pillA;
    int selected, hot = -1;

    public Segmented()
    {
        Height = Theme.P(40);
        Cursor = Cursors.Hand;
        pillA = new Anim(this, 0, 14);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Selected { get => selected; set { selected = value; pillA.To(value); } }

    float SegW => (Width - 2f * Theme.P(4)) / Items.Length;
    int IndexAt(int x) => Items.Length == 0 ? -1 : Math.Clamp((int)((x - Theme.P(4)) / SegW), 0, Items.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.X);
        if (i != hot) { hot = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = IndexAt(e.X);
        if (i >= 0 && i != selected) { Selected = i; SelectedChanged?.Invoke(); }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        float pad = Theme.P(4), w = SegW;
        Theme.Fill(g, Theme.Surface, new RectangleF(0, 0, Width, Height), Theme.P(11));
        Theme.Stroke(g, Theme.Line, new RectangleF(0, 0, Width, Height), Theme.P(11));
        var pill = new RectangleF(pad + (float)pillA.Value * w, pad, w, Height - 2 * pad);
        Theme.Fill(g, Theme.Hover, pill, Theme.P(8));

        for (int i = 0; i < Items.Length; i++)
        {
            var r = new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
            double near = 1 - Math.Min(Math.Abs(pillA.Value - i), 1);
            var size = TextRenderer.MeasureText(Items[i], Font);
            float dot = Theme.P(3.5f), total = 2 * dot + Theme.P(8) + size.Width, x = r.X + (r.Width - total) / 2;
            var col = Theme.Channel[Math.Min(i, Theme.Channel.Length - 1)];
            Theme.Dot(g, Theme.Mix(Color.FromArgb(110, col), col, near), x + dot, r.Y + r.Height / 2, dot);
            var fg = Theme.Mix(i == hot ? Theme.Text2 : Theme.Dim, Theme.Text, near);
            TextRenderer.DrawText(g, Items[i], Font, new Point((int)(x + 2 * dot + Theme.P(8)), (int)(r.Y + (r.Height - size.Height) / 2)), fg, TextFormatFlags.NoPrefix);
        }
    }
}

class FlatButton : DarkControl
{
    public bool Primary, Ghost, Dropdown;
    readonly Anim pressA;

    public FlatButton()
    {
        Height = Theme.P(38);
        Cursor = Cursors.Hand;
        TabStop = true;
        pressA = new Anim(this, 0, 30);
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressA.To(1); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressA.To(0); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        double hv = hoverA.Value, pr = pressA.Value;
        var rect = new RectangleF(0, 0, Width, Height);
        float rad = Theme.P(10);
        Color fg;
        if (!Enabled)
        {
            Theme.Stroke(g, Theme.Line, rect, rad);
            fg = Theme.Faint;
        }
        else if (Primary)
        {
            var bg = Theme.Mix(Theme.Mix(Theme.Accent, Color.FromArgb(0xff, 0xe4, 0x5c), hv), Color.FromArgb(0xd4, 0xb2, 0x00), pr);
            Theme.Fill(g, bg, rect, rad);
            fg = Color.FromArgb(0x16, 0x14, 0x0a);
        }
        else if (Ghost)
        {
            if (hv > 0 || pr > 0) Theme.Fill(g, Theme.Mix(Color.FromArgb(0, Theme.Ctl), Theme.Mix(Theme.Ctl, Theme.Hover, pr), Math.Max(hv, pr)), rect, rad);
            Theme.Stroke(g, Theme.Mix(Theme.Line, Theme.Hover, hv), rect, rad);
            fg = Theme.Mix(Theme.Text2, Theme.Text, hv);
        }
        else
        {
            Theme.Fill(g, Theme.Mix(Theme.Mix(Theme.Surface, Theme.Ctl, hv), Theme.Hover, pr), rect, rad);
            Theme.Stroke(g, Theme.Line, rect, rad);
            fg = Theme.Mix(Theme.Text2, Theme.Text, hv);
        }
        if (Focused && Enabled) Theme.Stroke(g, Color.FromArgb(140, Theme.Accent), rect, rad, Theme.P(1.5f));

        if (!Dropdown) { TextRenderer.DrawText(g, Text, Font, ClientRectangle, fg, AlignCenter); return; }
        var r = Rectangle.Inflate(ClientRectangle, -Theme.P(14), 0);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(r.X, r.Y, r.Width - Theme.P(18), r.Height), fg, AlignLeft);
        float cx = r.Right - Theme.P(5), cy = Height / 2f, s = Theme.P(4);
        using var pen = new Pen(Theme.Dim, Theme.P(1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, [new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2)]);
    }
}

class Toggle : DarkControl
{
    public string Hint = "";
    public event Action? CheckedChanged;
    readonly Anim knobA;
    bool on;

    public Toggle()
    {
        Height = Theme.P(32);
        Cursor = Cursors.Hand;
        knobA = new Anim(this, 0, 18);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => on; set { on = value; knobA.To(value ? 1 : 0, !IsHandleCreated); } }

    protected override void OnClick(EventArgs e)
    {
        Checked = !on;
        CheckedChanged?.Invoke();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        double k = knobA.Value;
        float w = Theme.P(36), h = Theme.P(20), x = Width - w - 1, y = (Height - h) / 2;
        int textW = (int)x - Theme.P(8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, textW, Height), Theme.Mix(Theme.Text2, Theme.Text, hoverA.Value), AlignLeft);
        if (Hint != "")
        {
            int tw = TextRenderer.MeasureText(Text, Font).Width;
            using var mono = Theme.Mono(8f);
            TextRenderer.DrawText(g, Hint, mono, new Rectangle(tw + Theme.P(4), 0, Math.Max(textW - tw - Theme.P(4), 0), Height), Theme.Faint, AlignLeft);
        }
        Theme.Fill(g, Theme.Mix(Theme.Hover, Theme.Accent, k), new RectangleF(x, y, w, h), h / 2);
        float d = h - Theme.P(6), kx = x + Theme.P(3) + (float)k * (w - d - Theme.P(6));
        Theme.Fill(g, Theme.Mix(Theme.Text2, Theme.Bg, k), new RectangleF(kx, y + Theme.P(3), d, d), d / 2);
    }
}

class ProfileList : DarkControl
{
    public List<(string Name, string Hint)> Items = new();
    public int Selected = -1;
    public event Action<int>? ItemClicked;
    int hot = -1, scroll;

    int RowH => Theme.P(40);
    int VisibleRows => Math.Max(Height / RowH, 1);
    int IndexAt(int y) { int i = y / RowH + scroll; return i < Items.Count ? i : -1; }

    public ProfileList() => Cursor = Cursors.Hand;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = IndexAt(e.Y);
        if (i != hot) { hot = i; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int i = IndexAt(e.Y);
        if (i >= 0 && e.Button == MouseButtons.Left) { Selected = i; Invalidate(); ItemClicked?.Invoke(i); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        scroll = Math.Clamp(scroll - Math.Sign(e.Delta), 0, Math.Max(Items.Count - VisibleRows, 0));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        scroll = Math.Clamp(scroll, 0, Math.Max(Items.Count - VisibleRows, 0));
        if (Items.Count == 0)
        {
            var box = new RectangleF(0, 0, Width, Height);
            using (var pen = new Pen(Theme.Line, Theme.P(1)) { DashPattern = [3, 3] })
            using (var path = Theme.Round(RectangleF.Inflate(box, -1, -1), Theme.P(12)))
                g.DrawPath(pen, path);
            using var big = Theme.DisplayLight(20f);
            var mid = Height / 2;
            TextRenderer.DrawText(g, "+", big, new Rectangle(0, mid - Theme.P(52), Width, Theme.P(40)), Theme.Faint, AlignCenter);
            TextRenderer.DrawText(g, "Henüz profil yok", Font, new Rectangle(0, mid - Theme.P(10), Width, Theme.P(22)), Theme.Text2, AlignCenter);
            using var small = Theme.Body(8.5f);
            TextRenderer.DrawText(g, "Ayarları beğenince Kaydet'e bas", small, new Rectangle(0, mid + Theme.P(12), Width, Theme.P(20)), Theme.Dim, AlignCenter);
            return;
        }
        using var mono = Theme.Mono(8f);
        for (int i = scroll; i < Math.Min(Items.Count, scroll + VisibleRows); i++)
        {
            var r = new Rectangle(0, (i - scroll) * RowH, Width, RowH - Theme.P(4));
            if (i == Selected) Theme.Fill(g, Theme.Ctl, r, Theme.P(10));
            else if (i == hot) Theme.Fill(g, Theme.Surface, r, Theme.P(10));
            if (i == Selected) Theme.Fill(g, Theme.Accent, new RectangleF(r.X, r.Y + Theme.P(10), Theme.P(3), r.Height - Theme.P(20)), Theme.P(1.5f));

            var t = Rectangle.Inflate(r, -Theme.P(14), 0);
            int hintW = 0;
            if (Items[i].Hint != "")
            {
                // Key-cap chip for the Ctrl+Alt+N hotkey.
                var hs = TextRenderer.MeasureText(Items[i].Hint, mono);
                float side = hs.Height + Theme.P(6);
                hintW = (int)Math.Max(side, hs.Width + Theme.P(10));
                var chip = new RectangleF(t.Right - hintW, r.Y + (r.Height - side) / 2f, hintW, side);
                Theme.Fill(g, i == Selected ? Theme.Hover : Theme.Surface, chip, Theme.P(5));
                Theme.Stroke(g, Theme.Line, chip, Theme.P(5));
                TextRenderer.DrawText(g, Items[i].Hint, mono, Rectangle.Round(chip), i == Selected ? Theme.Accent : Theme.Dim, AlignCenter);
            }
            TextRenderer.DrawText(g, Items[i].Name, Font, new Rectangle(t.X, t.Y, t.Width - hintW - Theme.P(8), t.Height), i == Selected ? Theme.Text : Theme.Text2, AlignLeft);
        }
    }
}

// Big live readout of the last thing touched; fades/slides in when the subject changes.
class Readout : DarkControl
{
    string title = "", value = "", unit = "";
    readonly Anim inA;

    public Readout() => inA = new Anim(this, 1, 12);

    public void Show(string t, string v, string u = "")
    {
        if (t == title && v == value && u == unit) return;
        bool sameTitle = t == title;
        (title, value, unit) = (t, v, u);
        if (!sameTitle) { inA.Value = 0; inA.To(1); }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        double a = inA.Value;
        Theme.Tracked(g, title, Theme.Caption, Theme.Mix(Theme.Bg, Theme.Dim, a), 0, 0, Theme.P(1.6f));
        const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        using var big = Theme.DisplayLight(30f);
        using var smaller = Theme.DisplayLight(20f);
        var font = big;
        var vs = TextRenderer.MeasureText(g, value, font, Size.Empty, f);
        if (vs.Width > Width - Theme.P(60)) { font = smaller; vs = TextRenderer.MeasureText(g, value, font, Size.Empty, f); }
        int y = Theme.P(24) + (int)((1 - a) * Theme.P(10));
        TextRenderer.DrawText(g, value, font, new Point(0, y), Theme.Mix(Theme.Bg, Theme.Text, a), f);
        if (unit == "") return;
        using var u = Theme.DisplayLight(15f);
        var us = TextRenderer.MeasureText(g, unit, u, Size.Empty, f);
        TextRenderer.DrawText(g, unit, u, new Point(vs.Width + Theme.P(6), y + vs.Height - us.Height - Theme.P(3)), Theme.Mix(Theme.Bg, Theme.Dim, a), f);
    }
}

class CurveView : DarkControl
{
    public Settings? Settings;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        var card = new RectangleF(0, 0, Width, Height);
        Theme.Fill(g, Theme.Surface, card, Theme.P(14));
        Theme.Stroke(g, Theme.Line, card, Theme.P(14));
        Theme.Tracked(g, "TON EĞRİSİ", Theme.Caption, Theme.Dim, Theme.P(16), Theme.P(14), Theme.P(1.6f));
        var a = RectangleF.FromLTRB(Theme.P(16), Theme.P(42), Width - Theme.P(16), Height - Theme.P(16));
        using (var grid = new Pen(Theme.Line))
            for (int i = 0; i <= 4; i++)
            {
                float x = a.X + a.Width * i / 4, y = a.Y + a.Height * i / 4;
                g.DrawLine(grid, x, a.Y, x, a.Bottom);
                g.DrawLine(grid, a.X, y, a.Right, y);
            }
        using (var dash = new Pen(Theme.Faint) { DashPattern = [2, 3] }) g.DrawLine(dash, a.X, a.Bottom, a.Right, a.Y);
        if (Settings is not { } s) return;

        var gains = Ramp.Gains(s.Kelvin);
        var pts = new PointF[3][];
        for (int c = 0; c < 3; c++)
        {
            pts[c] = new PointF[97];
            for (int i = 0; i <= 96; i++)
                pts[c][i] = new(a.X + a.Width * i / 96f, a.Bottom - (float)Ramp.Curve(s, gains[c], c, i / 96.0) * a.Height);
        }
        bool same = pts[1].SequenceEqual(pts[0]) && pts[2].SequenceEqual(pts[0]);
        for (int c = same ? 0 : 2; c >= 0; c--)
        {
            var col = same ? Theme.Accent : Theme.Channel[c + 1];
            using (var glow = new Pen(Color.FromArgb(45, col), Theme.P(6)) { LineJoin = LineJoin.Round }) g.DrawLines(glow, pts[c]);
            using (var pen = new Pen(col, Theme.P(2)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts[c]);
            Theme.Dot(g, col, pts[c][^1].X, pts[c][^1].Y, Theme.P(3));
        }
    }
}

// Calibration pattern; it is drawn through the real gamma ramp, so it shows the actual effect.
class PatternView : DarkControl
{
    Bitmap? cache;
    readonly Anim beforeA;
    bool before;

    public PatternView() => beforeA = new Anim(this, 0, 18);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Before { get => before; set { before = value; beforeA.To(value ? 1 : 0); } }

    void Build(Size size)
    {
        cache?.Dispose();
        cache = new Bitmap(Math.Max(size.Width, 1), Math.Max(size.Height, 1));
        using var g = Graphics.FromImage(cache);
        int w = cache.Width, h = cache.Height, y = 0;
        int Rows(double f) => (int)Math.Round(h * f);

        void Steps(int x0, int x1, int top, int height, Func<int, int> level)
        {
            for (int i = 0; i < 16; i++)
            {
                int v = level(i), a = x0 + (x1 - x0) * i / 16, b = x0 + (x1 - x0) * (i + 1) / 16;
                using var br = new SolidBrush(Color.FromArgb(v, v, v));
                g.FillRectangle(br, a, top, b - a, height);
            }
        }

        int h0 = Rows(0.30);
        Steps(0, w, 0, h0, i => i * 17);
        y += h0;
        foreach (var (c, f) in new[] { (Color.White, 0.13), (Color.Red, 0.11), (Color.Lime, 0.11), (Color.Blue, 0.11) })
        {
            int hh = Rows(f);
            using var br = new LinearGradientBrush(new Rectangle(-1, y, w + 2, hh), Color.Black, c, 0f);
            g.FillRectangle(br, 0, y, w, hh);
            y += hh;
        }
        Steps(0, w / 2, y, h - y, i => i * 2);        // near-black: crushed shadows merge
        Steps(w / 2, w, y, h - y, i => 225 + i * 2);  // near-white: clipped highlights merge
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        if (cache == null || cache.Size != Size) Build(Size);
        var card = new RectangleF(0, 0, Width, Height);
        using (var clip = Theme.Round(card, Theme.P(14)))
        {
            g.SetClip(clip);
            g.DrawImageUnscaled(cache!, 0, 0);
            g.ResetClip();
        }
        Theme.Stroke(g, Theme.Line, card, Theme.P(14));

        double b = beforeA.Value;
        string badge = b > 0.5 ? "ÖNCE" : "SONRA";
        using var font = Theme.BodySemi(8f);
        var size = TextRenderer.MeasureText(badge, font);
        var r = new RectangleF(Width - size.Width - Theme.P(34), Theme.P(12), size.Width + Theme.P(22), size.Height + Theme.P(8));
        Theme.Fill(g, Color.FromArgb(220, Theme.Mix(Theme.Accent, Theme.Side, b)), r, r.Height / 2);
        TextRenderer.DrawText(g, badge, font, Rectangle.Round(r), Theme.Mix(Theme.Side, Theme.Text, b), AlignCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) cache?.Dispose();
        base.Dispose(disposing);
    }
}

class StatusLine : DarkControl
{
    Color dot = Theme.Dim;
    readonly Anim pulseA;

    public StatusLine() => pulseA = new Anim(this, 0, 6);

    public void Set(string text, Color color)
    {
        if (text == Text && color == dot) return;
        Text = text;
        dot = color;
        pulseA.Value = 1;
        pulseA.To(0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = Begin(e);
        int tw = Math.Min(TextRenderer.MeasureText(Text, Font).Width, Width - Theme.P(20));
        float cx = Width - tw - Theme.P(12), cy = Height / 2f;
        Theme.Dot(g, Color.FromArgb((int)(70 * pulseA.Value), dot), cx, cy, Theme.P(3.5f) + (float)pulseA.Value * Theme.P(4));
        Theme.Dot(g, dot, cx, cy, Theme.P(3.5f));
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Width - tw, 0, tw, Height), Theme.Text2, AlignRight | TextFormatFlags.EndEllipsis);
    }
}

static class InputBox
{
    public static string? Ask(IWin32Window owner, string title, string value)
    {
        static int P(float v) => Theme.P(v);
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Bg, ForeColor = Theme.Text,
            Font = Theme.Body(10f), ClientSize = new Size(P(360), P(170)), Icon = Theme.MakeIcon(16),
        };
        var cap = new Caption("PROFİL ADI") { Bounds = new Rectangle(P(22), P(22), P(316), P(18)) };
        var frame = new Panel { Bounds = new Rectangle(P(22), P(46), P(316), P(42)), BackColor = Theme.Surface };
        var box = new TextBox
        {
            Text = value, Bounds = new Rectangle(P(12), P(11), P(292), P(22)), BackColor = Theme.Surface, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None, MaxLength = 40, Font = Theme.Body(10.5f),
        };
        frame.Controls.Add(box);
        frame.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Stroke(e.Graphics, box.Focused ? Theme.Accent : Theme.Hover, new RectangleF(0, 0, frame.Width, frame.Height), P(10));
        };
        box.GotFocus += (_, _) => frame.Invalidate();
        box.LostFocus += (_, _) => frame.Invalidate();
        var ok = new FlatButton { Text = "Kaydet", Primary = true, Bounds = new Rectangle(P(238), P(110), P(100), P(38)) };
        var cancel = new FlatButton { Text = "Vazgeç", Ghost = true, Bounds = new Rectangle(P(128), P(110), P(100), P(38)) };
        ok.Click += (_, _) => f.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => f.DialogResult = DialogResult.Cancel;
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel;
        };
        f.Controls.AddRange([cap, frame, ok, cancel]);
        f.HandleCreated += (_, _) => Win.DarkTitle(f.Handle);
        f.Shown += (_, _) => { box.Focus(); box.SelectAll(); };
        return f.ShowDialog(owner) == DialogResult.OK && box.Text.Trim() is { Length: > 0 } name ? name : null;
    }
}
