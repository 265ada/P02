using System.Reflection;

namespace P02;

public sealed class MainForm : Form
{
    private const int HotkeyId = 0xA02;
    private const int WM_HOTKEY = 0x0312;

    private readonly AppConfig _cfg;
    private readonly MonitorEngine _engine;
    private readonly GlobePanel _life;
    private readonly GlobePanel _mana;
    private readonly Button _arm = new();
    private readonly Label _status = new();
    private readonly Label _focus = new();
    private readonly TextBox _window = new();
    private readonly ComboBox _hotkey = new();
    private readonly NotifyIcon _tray = new();
    private bool _hotkeyRegistered;

    public MainForm(AppConfig cfg)
    {
        _cfg = cfg;
        _engine = new MonitorEngine(cfg);

        Text = $"P02  v{Version}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 430);

        _life = new GlobePanel("Life", cfg.Life, blue: false, Save) { Location = new Point(12, 12) };
        _mana = new GlobePanel("Mana", cfg.Mana, blue: true, Save) { Location = new Point(356, 12) };
        Controls.Add(_life);
        Controls.Add(_mana);

        int y = 274;

        _arm.SetBounds(12, y, 200, 54);
        _arm.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        _arm.Click += (_, _) => _engine.Toggle();
        Controls.Add(_arm);

        _status.SetBounds(224, y + 6, 460, 22);
        _status.Font = new Font("Segoe UI", 10);
        Controls.Add(_status);

        _focus.SetBounds(224, y + 30, 460, 20);
        _focus.ForeColor = SystemColors.GrayText;
        Controls.Add(_focus);

        y += 66;
        Controls.Add(new Label { Text = "Only fire while window title contains", Bounds = new Rectangle(12, y + 4, 220, 20) });
        _window.SetBounds(236, y, 190, 24);
        _window.Text = cfg.WindowMatch;
        _window.TextChanged += (_, _) => { _cfg.WindowMatch = _window.Text; Save(); };
        Controls.Add(_window);

        Controls.Add(new Label { Text = "Arm hotkey", Bounds = new Rectangle(444, y + 4, 70, 20) });
        _hotkey.SetBounds(518, y, 80, 24);
        _hotkey.DropDownStyle = ComboBoxStyle.DropDownList;
        _hotkey.Items.AddRange(Enumerable.Range(1, 12).Select(i => (object)$"F{i}").ToArray());
        _hotkey.SelectedItem = cfg.ArmHotkey;
        if (_hotkey.SelectedIndex < 0) _hotkey.SelectedIndex = 7;
        _hotkey.SelectedIndexChanged += (_, _) =>
        {
            _cfg.ArmHotkey = (string)_hotkey.SelectedItem!;
            Save();
            RegisterArmHotkey();
        };
        Controls.Add(_hotkey);

        y += 34;
        var logBtn = new Button { Text = "Open log folder", Bounds = new Rectangle(12, y, 130, 26) };
        logBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppConfig.Dir);
            System.Diagnostics.Process.Start("explorer.exe", AppConfig.Dir);
        };
        Controls.Add(logBtn);

        var updBtn = new Button { Text = "Check for updates", Bounds = new Rectangle(150, y, 140, 26) };
        updBtn.Click += async (_, _) => await Updater.CheckAsync(this, silent: false);
        Controls.Add(updBtn);

        var hint = new Label
        {
            Bounds = new Rectangle(300, y + 4, 390, 20),
            ForeColor = SystemColors.GrayText,
            Text = "Closing to tray keeps it running. Right-click the tray icon to quit.",
        };
        Controls.Add(hint);

        _engine.Sampled += OnSampled;
        _engine.ArmedChanged += _ => BeginInvoke(RefreshArmUi);
        _engine.Fired += (name, f) => Log.Write($"UI: {name} fired at {f:P1}");

        SetupTray();
        RefreshArmUi();
        _engine.Start();
    }

    private static string Version =>
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0] ?? "0.0.0";

    private void Save() => _cfg.Save();

    private void OnSampled(GlobeReading life, GlobeReading mana, bool focused)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _life.Update(life);
                _mana.Update(mana);
                _focus.Text = focused
                    ? "game window focused"
                    : $"waiting for a window titled like \"{_cfg.WindowMatch}\"";
            });
        }
        catch (ObjectDisposedException) { /* closing */ }
    }

    private void RefreshArmUi()
    {
        bool on = _engine.Armed;
        _arm.Text = on ? "ARMED  –  click to stop" : "DISARMED  –  click to arm";
        _arm.BackColor = on ? Color.FromArgb(200, 60, 60) : SystemColors.Control;
        _arm.ForeColor = on ? Color.White : SystemColors.ControlText;

        var watching = new List<string>();
        if (_cfg.Life.Enabled) watching.Add($"Life <{_cfg.Life.Threshold:P0} → {_cfg.Life.Key.ToUpperInvariant()}");
        if (_cfg.Mana.Enabled) watching.Add($"Mana <{_cfg.Mana.Threshold:P0} → {_cfg.Mana.Key.ToUpperInvariant()}");
        _status.Text = watching.Count == 0
            ? "No globe is switched on."
            : string.Join("     ", watching);

        _tray.Text = on ? "P02 – armed" : "P02 – disarmed";
    }

    // ---- tray ------------------------------------------------------------

    private void SetupTray()
    {
        _tray.Icon = SystemIcons.Shield;
        _tray.Visible = true;
        _tray.Text = "P02";
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Arm / disarm", null, (_, _) => _engine.Toggle());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => { _tray.Visible = false; Application.Exit(); });
        _tray.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    // ---- hotkey ----------------------------------------------------------

    private void RegisterArmHotkey()
    {
        if (_hotkeyRegistered)
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            _hotkeyRegistered = false;
        }
        // F1..F12 are VK 0x70..0x7B.
        if (!int.TryParse(_cfg.ArmHotkey.TrimStart('F', 'f'), out int n) || n is < 1 or > 12)
            n = 8;
        uint vk = (uint)(0x6F + n);
        _hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId, 0, vk);
        if (!_hotkeyRegistered)
            Log.Write($"could not register {_cfg.ArmHotkey} — another app owns it");
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterArmHotkey();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            _engine.Toggle();
        base.WndProc(ref m);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_cfg.CheckUpdatesOnStart)
            _ = Updater.CheckAsync(this, silent: true);
        if (_cfg.StartMinimised) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The X button hides to tray; Application.Exit really quits.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _engine.Dispose();
        if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, HotkeyId);
        _tray.Visible = false;
        base.OnFormClosing(e);
    }
}
