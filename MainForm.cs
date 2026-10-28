using System.ComponentModel;
using System.Globalization;
using Microsoft.Win32;

namespace TlkColor;

class MainForm : Form
{
    const uint HotMods = 0x0001 | 0x0002 | 0x4000; // Alt | Ctrl | NoRepeat
    const int HotReset = 10, HotToggle = 11, HotBefore = 12;
    static readonly CultureInfo Tr = new("tr-TR");
    static readonly string[] ChannelNames = ["Tümü", "Kırmızı", "Yeşil", "Mavi"];

    readonly Config cfg = Config.Load();
    readonly bool startHidden;
    bool hiddenStartDone, before, spaceHeld, quitting, trayHintShown, hotkeysOk = true;
    int channel; // 0 = all, 1..3 = R, G, B
    string target = ""; // "" = all screens
    readonly Dictionary<string, (int Vib, int Hue)> nvApplied = new(StringComparer.OrdinalIgnoreCase);

    readonly NotifyIcon tray;
    readonly ContextMenuStrip monitorMenu = Theme.Menu();
    readonly ToolTip tip = Theme.Tip();
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 600 }, reapplyTimer = new() { Interval = 1200 };

    readonly FlatButton monitorBtn, wideBtn, beforeBtn;
    readonly ProfileList profiles;
    readonly Toggle tEnabled;
    readonly Segmented seg;
    readonly Readout readout;
    readonly Slider sBright, sContrast, sGamma, sKelvin, sVib, sHue;
    readonly CurveView curve;
    readonly PatternView pattern;
    readonly StatusLine status;

    public MainForm(bool hidden)
    {
        startHidden = hidden;
        Theme.Scale = DeviceDpi / 96f;
        static int P(float v) => Theme.P(v);

        Text = "tlk-color";
        Icon = Theme.MakeIcon(32);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body(9.5f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(P(1040), P(664));
        KeyPreview = true;
        DoubleBuffered = true;

        // ── Sidebar ──────────────────────────────────────────────
        var side = new Panel { Bounds = new Rectangle(0, 0, P(264), ClientSize.Height), BackColor = Theme.Side };
        side.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.DrawMark(g, new RectangleF(P(24), P(28), P(22), P(22)));
            using var brand = Theme.DisplaySemi(15f);
            TextRenderer.DrawText(g, "tlk-color", brand, new Point(P(54), P(23)), Theme.Text, TextFormatFlags.NoPadding);
            using var sub = Theme.Body(8.5f);
            TextRenderer.DrawText(g, "Ekran renk ayarı", sub, new Point(P(55), P(52)), Theme.Dim, TextFormatFlags.NoPadding);
            using var pen = new Pen(Theme.Line);
            g.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);
        };
        Controls.Add(side);
        int sx = P(24), sw = P(216);

        side.Controls.Add(new Caption("EKRAN") { Bounds = new Rectangle(sx, P(100), sw, P(18)) });
        monitorBtn = new FlatButton { Dropdown = true, Bounds = new Rectangle(sx, P(122), sw, P(40)) };
        monitorBtn.Click += (_, _) => ShowMonitorMenu();
        side.Controls.Add(monitorBtn);

        side.Controls.Add(new Caption("PROFİLLER") { Bounds = new Rectangle(sx, P(188), sw, P(18)) });
        profiles = new ProfileList { Bounds = new Rectangle(sx, P(210), sw, P(200)) };
        profiles.ItemClicked += ApplyProfile;
        tip.SetToolTip(profiles, "Tıkla: uygula · Ctrl+Alt+1…9: kısayolla uygula");
        side.Controls.Add(profiles);
        int half = (sw - P(8)) / 2;
        var saveBtn = new FlatButton { Text = "Kaydet", Ghost = true, Bounds = new Rectangle(sx, P(418), half, P(36)) };
        var delBtn = new FlatButton { Text = "Sil", Ghost = true, Bounds = new Rectangle(sx + half + P(8), P(418), half, P(36)) };
        saveBtn.Click += (_, _) => SaveProfile();
        delBtn.Click += (_, _) => DeleteProfile();
        tip.SetToolTip(saveBtn, "Şu anki ayarları profil olarak kaydet");
        tip.SetToolTip(delBtn, "Seçili profili sil");
        side.Controls.AddRange([saveBtn, delBtn]);

        side.Controls.Add(new Caption("AYARLAR") { Bounds = new Rectangle(sx, P(480), sw, P(18)) });
        tEnabled = new Toggle { Text = "Etkin", Hint = "Ctrl+Alt+C", Checked = cfg.Enabled, Bounds = new Rectangle(sx, P(502), sw, P(32)) };
        tEnabled.CheckedChanged += () => SetEnabled(tEnabled.Checked);
        var tStartup = new Toggle { Text = "Windows ile başlat", Checked = Win.Autostart, Bounds = new Rectangle(sx, P(536), sw, P(32)) };
        tStartup.CheckedChanged += () => Win.Autostart = tStartup.Checked;
        var tTray = new Toggle { Text = "Kapatınca tepsiye", Checked = cfg.CloseToTray, Bounds = new Rectangle(sx, P(570), sw, P(32)) };
        tTray.CheckedChanged += () => { cfg.CloseToTray = tTray.Checked; cfg.Save(); };
        wideBtn = new FlatButton { Ghost = true, Bounds = new Rectangle(sx, P(610), sw, P(34)) };
        wideBtn.Click += (_, _) => EnableWideGamma();
        tip.SetToolTip(wideBtn, "Windows uç gama değerlerini kısar; bu ayar sınırı kaldırır (yönetici izni ister)");
        side.Controls.AddRange([tEnabled, tStartup, tTray, wideBtn]);

        // ── Header: live readout + channel switch ───────────────
        int mx = P(296), mw = P(712), col = (mw - P(32)) / 2;
        int segW = P(360);
        readout = new Readout { Bounds = new Rectangle(mx, P(26), mw - segW - P(24), P(80)) };
        seg = new Segmented { Items = ChannelNames, Bounds = new Rectangle(mx + mw - segW, P(44), segW, P(40)) };
        seg.SelectedChanged += () =>
        {
            channel = seg.Selected;
            LoadUi();
            readout.Show("KANAL", ChannelNames[channel]);
        };
        Controls.AddRange([readout, seg]);

        // ── Stage: what the screen actually shows ───────────────
        pattern = new PatternView { Bounds = new Rectangle(mx, P(120), mw - P(256), P(200)) };
        curve = new CurveView { Bounds = new Rectangle(mx + mw - P(240), P(120), P(240), P(200)) };
        tip.SetToolTip(pattern, "Test deseni ekranın gerçek çıktısıdır · alt sıradaki koyu ve açık kutuların hepsi ayırt edilebilmeli");
        Controls.AddRange([pattern, curve]);

        // ── Controls ────────────────────────────────────────────
        Controls.Add(new Caption("TON", true) { Bounds = new Rectangle(mx, P(346), col, P(18)) });
        Controls.Add(new Caption("RENK", true) { Bounds = new Rectangle(mx + col + P(32), P(346), col, P(18)) });
        int c1 = mx - P(9), c2 = mx + col + P(32) - P(9), cw = col + P(18);

        sBright = AddSlider("Parlaklık", 0, 100, 50, c1, P(372), cw, "%", (s, v) => { foreach (var c in Channels()) s.Brightness[c] = v; }, perChannel: true);
        sContrast = AddSlider("Kontrast", 0, 100, 50, c1, P(432), cw, "%", (s, v) => { foreach (var c in Channels()) s.Contrast[c] = v; }, perChannel: true);
        sGamma = AddSlider("Gama", 30, 280, 100, c1, P(492), cw, "", (s, v) => { foreach (var c in Channels()) s.Gamma[c] = v; }, perChannel: true);
        sGamma.Format = v => (v / 100.0).ToString("0.00", Tr);

        sKelvin = AddSlider("Renk sıcaklığı", 1900, 10000, 6500, c2, P(372), cw, " K", (s, v) => s.Kelvin = v, step: 100);
        sKelvin.Gradient = Enumerable.Range(0, 9).Select(i =>
        {
            var g = Ramp.Gains(1900 + i * 8100 / 8);
            return Color.FromArgb((int)(255 * g[0]), (int)(255 * g[1]), (int)(255 * g[2]));
        }).ToArray();
        sVib = AddSlider("Dijital canlılık", 0, 100, 50, c2, P(432), cw, "%", (s, v) => s.Vibrance = v);
        sVib.Gradient = [Color.FromArgb(0x6a, 0x6a, 0x6a), Color.FromArgb(0xb8, 0x8a, 0x5c), Color.FromArgb(0xff, 0x6a, 0x00)];
        sVib.Note = "NVIDIA ekran yok";
        sHue = AddSlider("Ton kayması", 0, 359, 0, c2, P(492), cw, "°", (s, v) => s.Hue = v);
        sHue.Gradient = [Color.FromArgb(255, 80, 80), Color.FromArgb(255, 220, 60), Color.FromArgb(80, 220, 100), Color.FromArgb(60, 210, 230), Color.FromArgb(80, 120, 255), Color.FromArgb(220, 80, 230), Color.FromArgb(255, 80, 80)];
        sHue.Note = "NVIDIA ekran yok";

        // ── Footer ──────────────────────────────────────────────
        beforeBtn = new FlatButton { Primary = true, Text = "Önce / Sonra  ·  basılı tut", Bounds = new Rectangle(mx, P(592), P(250), P(44)) };
        beforeBtn.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) SetBefore(true); };
        beforeBtn.MouseUp += (_, _) => SetBefore(false);
        tip.SetToolTip(beforeBtn, "Basılı tutunca orijinal renkler görünür · Boşluk tuşu da çalışır · Ctrl+Alt+B kilitler");
        var resetBtn = new FlatButton { Text = "Sıfırla", Ghost = true, Bounds = new Rectangle(mx + P(262), P(592), P(110), P(44)) };
        resetBtn.Click += (_, _) => Reset();
        tip.SetToolTip(resetBtn, "Seçili ekranı varsayılana döndür · Ctrl+Alt+0");
        status = new StatusLine { Bounds = new Rectangle(mx + P(384), P(592), mw - P(384), P(44)) };
        Controls.AddRange([beforeBtn, resetBtn, status]);

        tray = new NotifyIcon { Icon = Theme.MakeIcon(16), Text = "tlk-color", Visible = true, ContextMenuStrip = Theme.Menu() };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        tray.ContextMenuStrip.Opening += (_, e) => { BuildTrayMenu(); e.Cancel = false; };

        saveTimer.Tick += (_, _) => { saveTimer.Stop(); cfg.Save(); };
        reapplyTimer.Tick += (_, _) => { reapplyTimer.Stop(); Reload(true); };
        SystemEvents.DisplaySettingsChanged += OnSystemChange;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerChange;

        Reload(false);
        readout.Show("EKRAN", monitorBtn.Text);
    }

    Slider AddSlider(string label, int min, int max, int def, int x, int y, int w, string unit, Action<Settings, int> write, int step = 1, bool perChannel = false)
    {
        var s = new Slider { Label = label, Unit = unit, Min = min, Max = max, Default = def, Step = step, Bounds = new Rectangle(x, y, w, Theme.P(58)) };
        s.SetSilent(def, false);
        s.ValueChanged += () =>
        {
            foreach (var d in Targets()) write(cfg.For(d), s.Value);
            profiles.Selected = -1;
            profiles.Invalidate();
            Changed();
            ShowValue(s, perChannel);
        };
        s.MouseEnter += (_, _) => ShowValue(s, perChannel);
        s.GotFocus += (_, _) => ShowValue(s, perChannel);
        tip.SetToolTip(s, "Sürükle, tekerlek veya ok tuşları · çift tık: varsayılan");
        Controls.Add(s);
        return s;
    }

    void ShowValue(Slider s, bool perChannel)
    {
        if (!s.Enabled) return;
        var title = s.Label.ToUpper(Tr) + (perChannel && channel > 0 ? "  ·  " + ChannelNames[channel].ToUpper(Tr) : "");
        readout.Show(title, s.ValueText, s.Unit.Trim());
    }

    int[] Channels() => channel == 0 ? [0, 1, 2] : [channel - 1];
    IEnumerable<string> Targets() => target == "" ? Screen.AllScreens.Select(s => s.DeviceName) : [target];
    Settings Current() => cfg.Get(target == "" ? Screen.PrimaryScreen!.DeviceName : target);

    // Re-reads monitors and NVIDIA handles, then reapplies everything.
    void Reload(bool afterDisplayChange)
    {
        Nv.Refresh();
        nvApplied.Clear();
        // Monitors we never touched keep whatever vibrance/hue the NVIDIA panel set.
        foreach (var sc in Screen.AllScreens)
            if (!cfg.Monitors.ContainsKey(sc.DeviceName)) nvApplied[sc.DeviceName] = (50, 0);
        if (afterDisplayChange && target != "" && !Screen.AllScreens.Any(s => s.DeviceName == target)) target = "";
        LoadUi(false);
        ApplyAll();
    }

    void LoadUi(bool animate = true)
    {
        var s = Current();
        int c = Math.Max(channel - 1, 0);
        foreach (var sl in new[] { sBright, sContrast, sGamma }) sl.Accent = Theme.Channel[channel];
        sBright.SetSilent(s.Brightness[c], animate);
        sContrast.SetSilent(s.Contrast[c], animate);
        sGamma.SetSilent(s.Gamma[c], animate);
        sKelvin.SetSilent(s.Kelvin, animate);
        sVib.SetSilent(s.Vibrance, animate);
        sHue.SetSilent(s.Hue, animate);

        var t = Targets().ToList();
        sVib.Enabled = t.Any(Nv.HasVibrance);
        sHue.Enabled = t.Any(Nv.HasHue);

        curve.Settings = s;
        curve.Invalidate();
        var screens = Screen.AllScreens;
        int idx = Array.FindIndex(screens, x => x.DeviceName == target);
        monitorBtn.Text = idx < 0 ? $"Tüm ekranlar  ·  {screens.Length}" : MonitorName(screens[idx], idx);
        profiles.Items = cfg.Profiles.Select((p, i) => (p.Name, i < 9 ? $"{i + 1}" : "")).ToList();
        profiles.Invalidate();
        bool wide = Win.WideGamma;
        wideBtn.Text = wide ? "Geniş gama aralığı açık" : "Geniş gama aralığını aç";
        wideBtn.Enabled = !wide;
    }

    static string MonitorName(Screen s, int i) =>
        $"Ekran {i + 1}  ·  {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? "  ·  birincil" : "")}";

    void Changed()
    {
        curve.Settings = Current();
        curve.Invalidate();
        ApplyAll();
        saveTimer.Stop();
        saveTimer.Start();
    }

    void ApplyAll()
    {
        var worst = Ramp.Result.Ok;
        foreach (var sc in Screen.AllScreens)
        {
            var r = ApplyDevice(sc.DeviceName);
            if (r > worst) worst = r;
        }
        if (worst == Ramp.Result.Failed) status.Set("Ekran gama ayarını kabul etmedi", Theme.Danger);
        else if (worst == Ramp.Result.Limited) status.Set("Windows sınırı: etki kısıldı", Theme.Warn);
        else if (!hotkeysOk) status.Set("Bazı kısayollar başka programda", Theme.Warn);
        else if (before) status.Set("Orijinal renkler", Theme.Text2);
        else if (!cfg.Enabled) status.Set("Devre dışı", Theme.Faint);
        else status.Set("Uygulandı", Theme.Channel[2]);
    }

    Ramp.Result ApplyDevice(string dev)
    {
        var s = cfg.Enabled && !before ? cfg.Get(dev) : new Settings();
        var result = Ramp.Apply(dev, Ramp.Build(s));
        if (nvApplied.TryGetValue(dev, out var last) && last == (s.Vibrance, s.Hue)) return result;
        if (Nv.HasVibrance(dev)) Nv.SetVibrance(dev, s.Vibrance);
        if (Nv.HasHue(dev)) Nv.SetHue(dev, s.Hue);
        nvApplied[dev] = (s.Vibrance, s.Hue);
        return result;
    }

    void SetBefore(bool b)
    {
        if (before == b) return;
        before = b;
        pattern.Before = b;
        beforeBtn.Text = b ? "Önce  ·  bırakınca sonra" : "Önce / Sonra  ·  basılı tut";
        readout.Show("KARŞILAŞTIRMA", b ? "Önce" : "Sonra");
        ApplyAll();
    }

    void SetEnabled(bool on)
    {
        cfg.Enabled = on;
        tEnabled.Checked = on;
        ApplyAll();
        cfg.Save();
    }

    void Reset()
    {
        foreach (var d in Targets()) cfg.Monitors[d] = new Settings();
        profiles.Selected = -1;
        LoadUi();
        Changed();
        readout.Show("SIFIRLANDI", target == "" ? "Tüm ekranlar" : monitorBtn.Text);
    }

    void SaveProfile()
    {
        var name = InputBox.Ask(this, "Profil kaydet", $"Profil {cfg.Profiles.Count + 1}");
        if (name == null) return;
        int i = cfg.Profiles.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var profile = new Profile { Name = name, Settings = Current().Clone() };
        if (i >= 0) cfg.Profiles[i] = profile;
        else { cfg.Profiles.Add(profile); i = cfg.Profiles.Count - 1; }
        cfg.Save();
        LoadUi();
        profiles.Selected = i;
        readout.Show("PROFİL KAYDEDİLDİ", name);
    }

    void DeleteProfile()
    {
        int i = profiles.Selected;
        if (i < 0 || i >= cfg.Profiles.Count) { readout.Show("PROFİL", "Önce birini seç"); return; }
        var name = cfg.Profiles[i].Name;
        cfg.Profiles.RemoveAt(i);
        profiles.Selected = -1;
        cfg.Save();
        LoadUi();
        readout.Show("PROFİL SİLİNDİ", name);
    }

    void ApplyProfile(int i)
    {
        if (i < 0 || i >= cfg.Profiles.Count) return;
        foreach (var d in Targets()) cfg.Monitors[d] = cfg.Profiles[i].Settings.Clone();
        LoadUi();
        profiles.Selected = i;
        Changed();
        readout.Show("PROFİL", cfg.Profiles[i].Name);
    }

    void ShowMonitorMenu()
    {
        monitorMenu.Items.Clear();
        var screens = Screen.AllScreens;
        monitorMenu.Items.Add(new ToolStripMenuItem($"Tüm ekranlar  ·  {screens.Length}", null, (_, _) => SelectTarget("")) { Checked = target == "" });
        monitorMenu.Items.Add(new ToolStripSeparator());
        for (int i = 0; i < screens.Length; i++)
        {
            var dev = screens[i].DeviceName;
            var name = MonitorName(screens[i], i) + (Nv.HasVibrance(dev) ? "  ·  NVIDIA" : "");
            monitorMenu.Items.Add(new ToolStripMenuItem(name, null, (_, _) => SelectTarget(dev)) { Checked = target == dev });
        }
        monitorMenu.MinimumSize = new Size(monitorBtn.Width, 0);
        monitorMenu.Show(monitorBtn, new Point(0, monitorBtn.Height + Theme.P(6)));
    }

    void SelectTarget(string dev)
    {
        target = dev;
        profiles.Selected = -1;
        LoadUi();
        readout.Show("EKRAN", monitorBtn.Text);
    }

    void BuildTrayMenu()
    {
        var m = tray.ContextMenuStrip!;
        m.Items.Clear();
        m.Items.Add(new ToolStripMenuItem("Göster", null, (_, _) => ShowMain()) { Font = Theme.BodySemi(9.5f) });
        m.Items.Add(new ToolStripSeparator());
        for (int i = 0; i < cfg.Profiles.Count; i++)
        {
            int idx = i;
            m.Items.Add(new ToolStripMenuItem(cfg.Profiles[i].Name, null, (_, _) => ApplyProfile(idx)) { Checked = profiles.Selected == i });
        }
        if (cfg.Profiles.Count > 0) m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Etkin", null, (_, _) => SetEnabled(!cfg.Enabled)) { Checked = cfg.Enabled });
        m.Items.Add(new ToolStripMenuItem("Sıfırla", null, (_, _) => Reset()));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Çıkış", null, (_, _) => { quitting = true; Close(); }));
    }

    void EnableWideGamma()
    {
        try
        {
            Win.EnableWideGamma();
            LoadUi();
            ApplyAll();
            readout.Show("GENİŞ GAMA", Win.WideGamma ? "Açıldı" : "Yazılamadı");
        }
        catch (Win32Exception) { readout.Show("GENİŞ GAMA", "İzin verilmedi"); }
    }

    public void ShowMain()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void OnSystemChange(object? s, EventArgs e) => QueueReapply();
    void OnSessionSwitch(object? s, SessionSwitchEventArgs e) { if (e.Reason == SessionSwitchReason.SessionUnlock) QueueReapply(); }
    void OnPowerChange(object? s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) QueueReapply(); }

    // Display changes reset gamma ramps; wait for the driver to settle, then reapply.
    void QueueReapply()
    {
        if (IsHandleCreated) BeginInvoke(() => { reapplyTimer.Stop(); reapplyTimer.Start(); });
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden && !hiddenStartDone)
        {
            hiddenStartDone = true;
            if (!IsHandleCreated) CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win.DarkTitle(Handle);
        hotkeysOk = true;
        for (int i = 1; i <= 9; i++) hotkeysOk &= Win.RegisterHotKey(Handle, i, HotMods, (uint)(Keys.D0 + i));
        hotkeysOk &= Win.RegisterHotKey(Handle, HotReset, HotMods, (uint)Keys.D0);
        hotkeysOk &= Win.RegisterHotKey(Handle, HotToggle, HotMods, (uint)Keys.C);
        hotkeysOk &= Win.RegisterHotKey(Handle, HotBefore, HotMods, (uint)Keys.B);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) // WM_HOTKEY
        {
            int id = (int)m.WParam;
            if (id is >= 1 and <= 9) ApplyProfile(id - 1);
            else if (id == HotReset) Reset();
            else if (id == HotToggle) SetEnabled(!cfg.Enabled);
            else if (id == HotBefore) SetBefore(!before);
        }
        base.WndProc(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { spaceHeld = true; SetBefore(true); e.Handled = e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && spaceHeld) { spaceHeld = false; SetBefore(false); e.Handled = true; }
        base.OnKeyUp(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (spaceHeld) { spaceHeld = false; SetBefore(false); }
        base.OnDeactivate(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && cfg.CloseToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!trayHintShown) tray.ShowBalloonTip(2500, "tlk-color", "Tepside çalışmaya devam ediyor. Çıkmak için simgeye sağ tıkla.", ToolTipIcon.None);
            trayHintShown = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        saveTimer.Stop();
        cfg.Save();
        for (int i = 1; i <= HotBefore; i++) Win.UnregisterHotKey(Handle, i);
        SystemEvents.DisplaySettingsChanged -= OnSystemChange;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerChange;
        if (before) { before = false; ApplyAll(); } // never leave the screen stuck on "before"
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }
}
