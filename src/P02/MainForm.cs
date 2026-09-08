using System.Diagnostics;
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
    private readonly Label _live = new();
    private readonly NumericUpDown _pollHz = new();
    private bool _hotkeyRegistered;

    public MainForm(AppConfig cfg)
    {
        _cfg = cfg;
        _engine = new MonitorEngine(cfg);

        Text = $"P02  v{Version}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(772, 708);

        _life = new GlobePanel("Life", cfg.Life, blue: false, Save,
            () => _cfg.WindowMatch, () => _cfg.Mana.Region) { Location = new Point(12, 12) };
        _mana = new GlobePanel("Mana", cfg.Mana, blue: true, Save,
            () => _cfg.WindowMatch, () => _cfg.Life.Region) { Location = new Point(392, 12) };
        Controls.Add(_life);
        Controls.Add(_mana);

        int y = 512;

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

        var clearBtn = new Button { Text = "Any window", Bounds = new Rectangle(430, y, 86, 24) };
        clearBtn.Click += (_, _) => _window.Text = "";
        Controls.Add(clearBtn);

        Controls.Add(new Label
        {
            Text = "Polls/sec",
            Bounds = new Rectangle(524, y + 4, 60, 20),
        });
        _pollHz.SetBounds(586, y, 64, 24);
        _pollHz.Minimum = 5;
        _pollHz.Maximum = 250;
        _pollHz.Increment = 5;
        _pollHz.Value = Math.Clamp(cfg.PollHz, 5, 250);
        _pollHz.ValueChanged += (_, _) => { _cfg.PollHz = (int)_pollHz.Value; Save(); };
        Controls.Add(_pollHz);

        Controls.Add(new Label { Text = "Arm key", Bounds = new Rectangle(658, y + 4, 50, 20) });
        _hotkey.SetBounds(708, y, 52, 24);
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
        updBtn.Click += async (_, _) =>
            await Updater.CheckAsync(this, silent: false, beforeExit: _cfg.SaveNow);
        Controls.Add(updBtn);

        var upd = new CheckBox
        {
            Text = "Check at launch",
            Bounds = new Rectangle(436, y + 3, 118, 22),
            Checked = cfg.CheckUpdatesOnStart,
        };
        upd.CheckedChanged += (_, _) =>
        { _cfg.CheckUpdatesOnStart = upd.Checked; Save(); };
        Controls.Add(upd);

        var hide = new CheckBox
        {
            Text = "Hide from screen capture",
            Bounds = new Rectangle(560, y + 3, 200, 22),
            Checked = cfg.HideFromCapture,
        };
        hide.CheckedChanged += (_, _) =>
        {
            _cfg.HideFromCapture = hide.Checked;
            Save();
            Native.ExcludeFromCapture(Handle, hide.Checked);
            if (hide.Checked)
                MessageBox.Show(this,
                    "This window is now invisible to screen capture of any kind - "
                    + "screenshots, the Snipping Tool, Discord and OBS included."
                    + Environment.NewLine + Environment.NewLine
                    + "It stops P02 being read as a globe if it covers one. Untick it "
                    + "before trying to screenshot or share the window.",
                    "Hide from screen capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        Controls.Add(hide);


        var diagBtn = new Button
        {
            Text = "Export diagnostics",
            Bounds = new Rectangle(298, y, 130, 26),
        };
        diagBtn.Click += (_, _) => ExportDiagnostics();
        Controls.Add(diagBtn);

        var sound = new CheckBox
        {
            Text = "Ding on fire",
            Bounds = new Rectangle(130, y + 35, 96, 22),
            Checked = cfg.SoundOnFire,
        };
        sound.CheckedChanged += (_, _) =>
        {
            _cfg.SoundOnFire = sound.Checked;
            Save();
            if (sound.Checked) _engine.TestSound();
        };
        Controls.Add(sound);

        Controls.Add(new Label
        {
            Text = "no more often than",
            Bounds = new Rectangle(232, y + 38, 108, 20),
        });
        var gap = new NumericUpDown { Bounds = new Rectangle(342, y + 34, 56, 24) };
        gap.Minimum = 0;
        gap.Maximum = 120;
        gap.Value = Math.Clamp(cfg.SoundGapMs / 1000, 0, 120);
        gap.ValueChanged += (_, _) => { _cfg.SoundGapMs = (int)gap.Value * 1000; Save(); };
        Controls.Add(gap);
        Controls.Add(new Label
        {
            Text = "seconds",
            Bounds = new Rectangle(402, y + 38, 60, 20),
        });

        Controls.Add(new Label
        {
            Text = "volume",
            Bounds = new Rectangle(466, y + 38, 48, 20),
        });
        var vol = new NumericUpDown { Bounds = new Rectangle(516, y + 34, 56, 24) };
        vol.Minimum = -24;
        vol.Maximum = MonitorEngine.MaxGainDb;
        vol.Value = Math.Clamp(cfg.SoundGainDb, -24, MonitorEngine.MaxGainDb);
        vol.ValueChanged += (_, _) =>
        {
            _cfg.SoundGainDb = (int)vol.Value;
            Save();
            // Play at the new level so it can be judged by ear.
            if (_cfg.SoundOnFire) _engine.SetSoundGain(_cfg.SoundGainDb);
        };
        Controls.Add(vol);
        Controls.Add(new Label
        {
            Text = $"dB (max +{MonitorEngine.MaxGainDb})",
            Bounds = new Rectangle(576, y + 38, 110, 20),
        });

        y += 32;
        var testBtn = new Button { Text = "Test keys (3s)", Bounds = new Rectangle(12, y, 110, 26) };
        testBtn.Click += (_, _) => TestKeys();
        Controls.Add(testBtn);

        y += 32;
        _live.SetBounds(12, y, 748, 20);
        _live.ForeColor = SystemColors.GrayText;
        Controls.Add(_live);

        _engine.Sampled += OnSampled;
        _engine.ArmedChanged += _ => BeginInvoke(RefreshArmUi);
        _engine.WouldFire += (name, frac) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() => (name == "Life" ? _life : _mana).ShowWouldFire(frac));
            }
            catch (ObjectDisposedException) { /* closing */ }
        };

        _engine.EffectChecked += (name, worked, streak) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    var panel = name == "Life" ? _life : _mana;
                    panel.ShowEffect(worked, streak);
                });
            }
            catch (ObjectDisposedException) { /* closing */ }
        };

        // Come back where it was left, unless that screen has since gone away.
        if (cfg.WindowX >= 0 && cfg.WindowY >= 0)
        {
            var spot = new Rectangle(cfg.WindowX, cfg.WindowY, Width, Height);
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(spot)))
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(cfg.WindowX, cfg.WindowY);
            }
        }

        SetupTray();
        RefreshArmUi();
        _engine.Start();
    }

    private static string Version =>
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>Any settings edit: persist it and re-render the summary line.
    /// Without the refresh, ticking a globe on while armed left the status text
    /// stale and it looked like arming had been lost.</summary>
    private void Save()
    {
        _cfg.Save();
        RefreshArmUi();
    }

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
                    ? "game window focused — firing allowed"
                    : "not firing: focused window does not match";
                _focus.ForeColor = focused
                    ? Color.FromArgb(0, 120, 0)
                    : SystemColors.GrayText;
                string covering = CoveredGlobes();
                _live.Text = covering.Length > 0
                    ? $"This window is covering the {covering} globe - move it, or the "
                      + "capture reads P02 instead of the globe."
                    : $"focused window: \"{_engine.ForegroundTitle}\"     "
                      + $"polls/sec: {_engine.ActualHz}     "
                      + $"work per poll: {_engine.LastPollMs:0.0} ms";
                _live.ForeColor = covering.Length > 0
                    ? Color.FromArgb(190, 60, 0)
                    : SystemColors.GrayText;
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
        Native.ExcludeFromCapture(Handle, _cfg.HideFromCapture);
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
            _ = Updater.CheckAsync(this, silent: true, beforeExit: _cfg.SaveNow);
        if (_cfg.StartMinimised) Hide();
    }

    /// <summary>
    /// Sends each switched-on globe's key once, ignoring arm state and the
    /// window match, so a keybind that never reaches the game shows up as a
    /// setup problem rather than a detection one.
    /// </summary>
    private void TestKeys()
    {
        var keys = new List<string>();
        if (_cfg.Life.Enabled) keys.Add(_cfg.Life.Key);
        if (_cfg.Mana.Enabled) keys.Add(_cfg.Mana.Key);
        if (keys.Count == 0)
        {
            MessageBox.Show(this, "Switch on a globe first.", "Test keys",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _status.Text = $"clicking into the game… sending {string.Join(" and ", keys)} in 3s";
        var t = new System.Windows.Forms.Timer { Interval = 3000 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            if (_cfg.Life.Enabled) _engine.TestKey(_cfg.Life.Key, _cfg.Life.HoldMs);
            if (_cfg.Mana.Enabled) _engine.TestKey(_cfg.Mana.Key, _cfg.Mana.HoldMs);
            Log.Write($"test keys sent: {string.Join(", ", keys)}");
            BeginInvoke(RefreshArmUi);
        };
        t.Start();
    }

    /// <summary>
    /// Writes a shareable bundle: a readable report, the recent log, the
    /// config, and what the detector currently sees in each globe.
    /// </summary>
    private void ExportDiagnostics()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            string zip = Diagnostics.Export(_cfg, _engine, this);
            Cursor = Cursors.Default;

            string msg = "Diagnostics written to:" + Environment.NewLine + zip
                + Environment.NewLine + Environment.NewLine
                + "The .txt beside it is the same report in plain text, ready to paste. "
                + "Your update token is not included."
                + Environment.NewLine + Environment.NewLine
                + "Open the folder now?";
            if (MessageBox.Show(this, msg, "Export diagnostics",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start("explorer.exe", $"/select,\"{zip}\"");
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            Log.Write($"diagnostics export failed: {ex}");
            MessageBox.Show(this, ex.Message, "Export diagnostics",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Capture reads the screen, so a P02 window sitting over a globe is read
    /// as the globe. Saying so beats the old fix of hiding the window from
    /// every capture on the system.
    /// </summary>
    private string CoveredGlobes()
    {
        if (!Visible || WindowState == FormWindowState.Minimized || _cfg.HideFromCapture)
            return string.Empty;

        var names = new List<string>();
        if (_cfg.Life.Enabled && _cfg.Life.Region.IsValid
            && Bounds.IntersectsWith(_cfg.Life.Region.ToRect())) names.Add("Life");
        if (_cfg.Mana.Enabled && _cfg.Mana.Region.IsValid
            && Bounds.IntersectsWith(_cfg.Mana.Region.ToRect())) names.Add("Mana");
        return string.Join(" and ", names);
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
        if (WindowState == FormWindowState.Normal)
        {
            _cfg.WindowX = Location.X;
            _cfg.WindowY = Location.Y;
        }
        _cfg.SaveNow();
        if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, HotkeyId);
        _tray.Visible = false;
        base.OnFormClosing(e);
    }
}
