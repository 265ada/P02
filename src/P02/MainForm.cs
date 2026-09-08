using System.Diagnostics;
using System.Reflection;

namespace P02;

public sealed class MainForm : Form
{
    private const int HotkeyId = 0xA02;
    private const int WM_HOTKEY = 0x0312;

    private readonly AppConfig _cfg;
    private readonly MonitorEngine _engine;
    private readonly System.Windows.Forms.Timer _critical = new();
    private readonly System.Windows.Forms.Timer _watch = new();
    private int _criticalLeft = 31;
    private bool _criticalDone;

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
    private readonly Button _pin = new();
    private OverlayForm? _overlay;
    private bool _hotkeyRegistered;

    public MainForm(AppConfig cfg)
    {
        _cfg = cfg;
        _engine = new MonitorEngine(cfg);

        Text = $"P02  v{Version}";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 856);

        _pin.SetBounds(864, 6, 24, 22);
        _pin.Text = "P";
        _pin.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        _pin.FlatStyle = FlatStyle.System;
        var tip = new ToolTip();
        Tips.On(_pin, Tips.Pin);
        _pin.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ResetOverlay(); };
        _critical.Interval = 1000;
        _critical.Tick += (_, _) => OnCriticalTick();

        // One small request every five minutes, and no window unless something
        // is actually wrong. A release that fixes a way to die quietly is no
        // use sitting on a server while somebody plays the version it fixes.
        _watch.Interval = 5 * 60 * 1000;
        _watch.Tick += (_, _) =>
        {
            if (_criticalDone || Updater.Critical is not null) return;
            _ = Updater.CheckAsync(this, silent: true, beforeExit: _cfg.SaveNow, ui: false);
        };

        _pin.Click += (_, _) => ToggleOverlay(!(_overlay?.Visible ?? false));
        Controls.Add(_pin);

        Controls.Add(Cap(new Label
        {
            Text = "pin a small readout over the game",
            Bounds = new Rectangle(620, 10, 240, 18),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText,
        }, Tips.Pin));

        var snap = new CheckBox
        {
            Text = "Snap to the game's numbers",
            Bounds = new Rectangle(392, 8, 190, 22),
            Checked = cfg.OverlaySnap,
        };
        snap.CheckedChanged += (_, _) =>
        {
            _cfg.OverlaySnap = snap.Checked;
            Save();
            if (_overlay is { IsDisposed: false }) _overlay.Locked = snap.Checked;
            PlaceOverlay();
        };
        Controls.Add(snap);
        Tips.On(snap, Tips.OverlaySnap);

        var autoHide = new CheckBox
        {
            Text = "Hide it when they are covered",
            Bounds = new Rectangle(190, 8, 200, 22),
            Checked = cfg.OverlayAutoHide,
        };
        autoHide.CheckedChanged += (_, _) => { _cfg.OverlayAutoHide = autoHide.Checked; Save(); };
        Controls.Add(autoHide);
        Tips.On(autoHide, Tips.OverlayAutoHide);

        var probe = new TextProbe(_engine.TextAvailable, _engine.TextUnavailable,
                                  _engine.ProbeText);

        _life = new GlobePanel("Life", cfg.Life, blue: false, Save,
            () => _cfg.WindowMatch, () => _cfg.Mana.Region, probe, cfg.Shield, FindAllNumbers)
            { Location = new Point(12, 36) };
        _mana = new GlobePanel("Mana", cfg.Mana, blue: true, Save,
            () => _cfg.WindowMatch, () => _cfg.Life.Region, probe, null, FindAllNumbers)
            { Location = new Point(406, 36) };
        Controls.Add(_life);
        Controls.Add(_mana);

        int y = 622;

        _arm.SetBounds(12, y, 200, 54);
        _arm.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        _arm.Click += (_, _) => _engine.Toggle();
        Controls.Add(_arm);
        Tips.On(_arm, "Arms and disarms. Nothing is ever sent while disarmed.",
            "", "It starts disarmed every launch, on purpose.");

        _status.SetBounds(224, y + 6, 460, 22);
        _status.Font = new Font("Segoe UI", 10);
        Controls.Add(_status);
        Tips.On(_status, Tips.Status);

        _focus.SetBounds(224, y + 30, 460, 20);
        _focus.ForeColor = SystemColors.GrayText;
        Controls.Add(_focus);
        Tips.On(_focus, Tips.Focus);

        y += 66;
        Controls.Add(Cap(new Label { Text = "Only fire while window title contains", Bounds = new Rectangle(12, y + 4, 220, 20) }, Tips.WindowMatch));
        _window.SetBounds(236, y, 190, 24);
        _window.Text = cfg.WindowMatch;
        _window.TextChanged += (_, _) => { _cfg.WindowMatch = _window.Text; Save(); };
        Controls.Add(_window);
        Tips.On(_window, Tips.WindowMatch);

        var clearBtn = new Button { Text = "Any window", Bounds = new Rectangle(430, y, 86, 24) };
        clearBtn.Click += (_, _) => _window.Text = "";
        Controls.Add(clearBtn);
        Tips.On(clearBtn, Tips.AnyWindow);

        Controls.Add(Cap(new Label
        {
            Text = "Polls/sec",
            Bounds = new Rectangle(524, y + 4, 60, 20),
        }, Tips.PollHz));
        _pollHz.SetBounds(586, y, 64, 24);
        _pollHz.Minimum = 5;
        _pollHz.Maximum = 250;
        _pollHz.Increment = 5;
        _pollHz.Value = Math.Clamp(cfg.PollHz, 5, 250);
        _pollHz.ValueChanged += (_, _) => { _cfg.PollHz = (int)_pollHz.Value; Save(); };
        Controls.Add(_pollHz);
        Tips.On(_pollHz, Tips.PollHz);

        Controls.Add(Cap(new Label { Text = "Arm key", Bounds = new Rectangle(658, y + 4, 50, 20) }, Tips.ArmKey));
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
        Tips.On(_hotkey, Tips.ArmKey);

        y += 34;
        var logBtn = new Button { Text = "Open log folder", Bounds = new Rectangle(12, y, 130, 26) };
        logBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppConfig.Dir);
            System.Diagnostics.Process.Start("explorer.exe", AppConfig.Dir);
        };
        Controls.Add(logBtn);
        Tips.On(logBtn, Tips.OpenLog);

        var updBtn = new Button { Text = "Check for updates", Bounds = new Rectangle(150, y, 140, 26) };
        updBtn.Click += async (_, _) =>
            await Updater.CheckAsync(this, silent: false, beforeExit: _cfg.SaveNow);
        Controls.Add(updBtn);
        Tips.On(updBtn, Tips.CheckUpdates);

        var findAll = new Button
        {
            Text = "Find numbers",
            Bounds = new Rectangle(298, y, 130, 26),
        };
        findAll.Click += (_, _) => FindAllNumbers();
        Controls.Add(findAll);

        Tips.On(findAll, Tips.FindNumbers);


        var upd = new CheckBox
        {
            Text = "Check at launch",
            Bounds = new Rectangle(586, y + 3, 118, 22),
            Checked = cfg.CheckUpdatesOnStart,
        };
        upd.CheckedChanged += (_, _) =>
        { _cfg.CheckUpdatesOnStart = upd.Checked; Save(); };
        Controls.Add(upd);
        Tips.On(upd, Tips.UpdateAtLaunch);

        var hide = new CheckBox
        {
            Text = "Hide from capture",
            Bounds = new Rectangle(712, y + 3, 140, 22),
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
        Tips.On(hide, Tips.HideCapture);


        var diagBtn = new Button
        {
            Text = "Export diagnostics",
            Bounds = new Rectangle(436, y, 140, 26),
        };
        diagBtn.Click += (_, _) => ExportDiagnostics();
        Controls.Add(diagBtn);

        var shareBtn = new Button
        {
            Text = "Share settings",
            Bounds = new Rectangle(diagBtn.Right + 8, diagBtn.Top, 116, 26),
        };
        shareBtn.Click += (_, _) => ShareSettings();
        Controls.Add(shareBtn);
        Tips.On(shareBtn, Tips.ShareSettings);

        var applyBtn = new Button
        {
            Text = "Apply shared",
            Bounds = new Rectangle(diagBtn.Right + 130, diagBtn.Top, 110, 26),
        };
        applyBtn.Click += (_, _) => ApplyShared();
        Controls.Add(applyBtn);
        Tips.On(applyBtn, Tips.ApplyShared);
        Tips.On(diagBtn, Tips.Diagnostics);

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
        Tips.On(sound, Tips.Ding);

        Controls.Add(Cap(new Label
        {
            Text = "no more often than",
            Bounds = new Rectangle(232, y + 38, 108, 20),
        }, Tips.DingGap));
        var gap = new NumericUpDown { Bounds = new Rectangle(342, y + 34, 62, 24) };
        gap.Minimum = 0;
        gap.Maximum = 120000;
        gap.Increment = 100;
        gap.Value = Math.Clamp(cfg.SoundGapMs, 0, 120000);
        gap.ValueChanged += (_, _) => { _cfg.SoundGapMs = (int)gap.Value; Save(); };
        Controls.Add(gap);
        Tips.On(gap, Tips.DingGap);
        Controls.Add(Cap(new Label
        {
            Text = "ms",
            Bounds = new Rectangle(408, y + 38, 26, 20),
        }, Tips.DingGap));

        var disarmedDing = new CheckBox
        {
            Text = "when disarmed",
            Bounds = new Rectangle(700, y + 36, 116, 22),
            Checked = cfg.SoundWhenDisarmed,
        };
        disarmedDing.CheckedChanged += (_, _) =>
        { _cfg.SoundWhenDisarmed = disarmedDing.Checked; Save(); };
        Controls.Add(disarmedDing);
        Tips.On(disarmedDing, Tips.DingDisarmed);

        Controls.Add(Cap(new Label
        {
            Text = "volume",
            Bounds = new Rectangle(440, y + 38, 48, 20),
        }, Tips.Volume));
        var vol = new NumericUpDown { Bounds = new Rectangle(490, y + 34, 56, 24) };
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
        Tips.On(vol, Tips.Volume);
        Controls.Add(Cap(new Label
        {
            Text = $"dB (max +{MonitorEngine.MaxGainDb})",
            Bounds = new Rectangle(550, y + 38, 110, 20),
        }, Tips.Volume));

        y += 32;
        var rescan = new Button
        {
            Text = "Re-scan",
            Bounds = new Rectangle(766, y + 32, 96, 26),
        };
        rescan.Click += (_, _) => { _engine.RescanMemory(); Log.Write("memory: manual rescan"); };
        Controls.Add(rescan);
        Tips.On(rescan, Tips.Rescan);

        var testBtn = new Button { Text = "Test keys (3s)", Bounds = new Rectangle(12, y, 110, 26) };
        testBtn.Click += (_, _) => TestKeys();
        Controls.Add(testBtn);
        Tips.On(testBtn, Tips.TestKeys);

        y += 32;
        Controls.Add(Cap(new Label { Text = "Send by", Bounds = new Rectangle(12, y + 4, 50, 20) }, Tips.SendBy));
        var method = new ComboBox
        {
            Bounds = new Rectangle(64, y, 150, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        method.Items.AddRange(["Injected input", "Posted to window"]);
        method.SelectedIndex = cfg.InputMethod.Equals("postmessage",
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        method.SelectedIndexChanged += (_, _) =>
        {
            _cfg.InputMethod = method.SelectedIndex == 1 ? "postmessage" : "sendinput";
            Save();
        };
        Controls.Add(method);
        Tips.On(method, Tips.SendBy);

        Controls.Add(Cap(new Label
        {
            Text = "posted reaches an unfocused window; try it if injected is ignored",
            Bounds = new Rectangle(220, y + 4, 380, 20),
            ForeColor = SystemColors.GrayText,
        }, Tips.SendBy));

        var mem = new CheckBox
        {
            Text = "Read game memory",
            Bounds = new Rectangle(608, y + 2, 150, 22),
            Checked = cfg.UseMemory,
        };
        mem.CheckedChanged += (_, _) =>
        {
            if (mem.Checked && MessageBox.Show(this,
                    "This reads life and mana straight out of the game's memory. It is exact "
                    + "and instant, and it is the most intrusive thing here by a distance: "
                    + "reading another process is what anti-cheat looks for, where watching "
                    + "the screen is passive."
                    + Environment.NewLine + Environment.NewLine
                    + "It finds the values by searching for their shape rather than using "
                    + "fixed offsets, so it survives patches, and it takes a couple of "
                    + "seconds on first use."
                    + Environment.NewLine + Environment.NewLine + "Turn it on?",
                    "Read game memory", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                mem.Checked = false;
                return;
            }
            _engine.SetMemory(mem.Checked);
            Save();
        };
        Controls.Add(mem);
        Tips.On(mem, Tips.Memory);


        y += 32;
        _live.SetBounds(12, y, 876, 20);
        _live.ForeColor = SystemColors.GrayText;
        Controls.Add(_live);
        Tips.On(_live, Tips.Live);

        _engine.Sampled += OnSampled;
        _engine.ArmedChanged += _ => BeginInvoke(RefreshArmUi);
        _engine.Fired += (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (_overlay is { IsDisposed: false, Visible: true }) _overlay.Fired();
                });
            }
            catch (ObjectDisposedException) { /* closing */ }
        };

        _engine.MaxAdopted += (name, was, now) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (name == "Life") _life.MaxAdopted(was, now);
                    else if (name == "Mana") _mana.MaxAdopted(was, now);
                    _cfg.SaveNow();
                });
            }
            catch (ObjectDisposedException) { /* closing */ }
        };

        _engine.Blind += (name, blind) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() => (name == "Life" ? _life : _mana).ShowBlind(blind));
            }
            catch (ObjectDisposedException) { /* closing */ }
        };

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

        _lastKnownLife = cfg.Life.KnownMax;
        _lastKnownMana = cfg.Mana.KnownMax;

        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        Theme.Apply(this);

        _life.Accent = Theme.Bad;
        _mana.Accent = Theme.Accent;
        _arm.Font = Theme.Big;
        _status.Font = Theme.UiBold;
        _live.Font = Theme.Small;
        _pin.Font = Theme.UiBold;

        SetupTray();
        if (cfg.OverlayOn) ToggleOverlay(true);

        RefreshArmUi();
        _engine.Start();
    }

    /// <summary>
    /// A caption carrying the same explanation as the field it names. The
    /// caption is the part you read, so it is the part the pointer lands on,
    /// and finding nothing there reads as nothing to find.
    /// </summary>
    private static Label Cap(Label l, params string[] tip)
    {
        Tips.On(l, tip);
        return l;
    }

    /// <summary>Puts this setup on the clipboard, and in a file beside the log.</summary>
    private void ShareSettings()
    {
        string text = SettingsShare.Export(_cfg, Version);
        string path = Path.Combine(AppConfig.Dir, $"P02-settings-{Version}.txt");

        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            File.WriteAllText(path, text);
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not write the settings out: {ex.Message}",
                            "Share settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Log.Write($"settings exported to {path}");
        MessageBox.Show(this,
            "Copied to the clipboard, and saved as:" + Environment.NewLine
            + path + Environment.NewLine + Environment.NewLine
            + "It carries every setting except the screen regions and your own "
            + "maxima - those belong to this machine and this character, and are "
            + "found again wherever it is loaded."
            + Environment.NewLine + Environment.NewLine
            + $"It only loads into v{Version}.",
            "Share settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Reads a shared block off the clipboard and applies it.</summary>
    private void ApplyShared()
    {
        string text = "";
        try { text = Clipboard.GetText(); } catch { /* nothing on it */ }

        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this,
                "Copy the exported settings text first - all of it, including the "
                + "first line.",
                "Apply shared settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? why = SettingsShare.Import(text, _cfg, Version);
        if (why is not null)
        {
            MessageBox.Show(this, why, "Apply shared settings",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cfg.SaveNow();
        Log.Write("settings imported from the clipboard");

        MessageBox.Show(this,
            "Settings applied. P02 will restart to pick them up - it comes back "
            + "disarmed, so arm it when you are ready.",
            "Apply shared settings", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Application.Restart();
        Environment.Exit(0);
    }

    private static string Version =>
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>Any settings edit: persist it and re-render the summary line.
    /// Without the refresh, ticking a globe on while armed left the status text
    /// stale and it looked like arming had been lost.</summary>
    private int _lastKnownLife, _lastKnownMana;

    private void Save()
    {
        // The life flask is what recovers energy shield, so the shield watcher
        // uses the same key and the same timing rather than a second set of
        // settings that could quietly disagree with it.
        _cfg.Shield.Key = _cfg.Life.Key;
        _cfg.Shield.HoldMs = _cfg.Life.HoldMs;
        _cfg.Shield.CooldownMs = _cfg.Life.CooldownMs;
        _cfg.Shield.PanicCooldownMs = _cfg.Life.PanicCooldownMs;
        _cfg.Shield.BurstCount = _cfg.Life.BurstCount;
        _cfg.Shield.BurstGapMs = _cfg.Life.BurstGapMs;
        // Panic is a rule about how hard you are being hit, not about which
        // pool is being hit, so the shield uses the same one as life.
        _cfg.Shield.PanicBelow = _cfg.Life.PanicBelow;
        _cfg.Shield.UberBelow = _cfg.Life.UberBelow;
        _cfg.Shield.FastDropPctPerSec = _cfg.Life.FastDropPctPerSec;
        _cfg.Shield.ConfirmFrames = _cfg.Life.ConfirmFrames;
        _cfg.Shield.IgnoreBelow = _cfg.Life.IgnoreBelow;
        _cfg.Shield.BlindGraceMs = _cfg.Life.BlindGraceMs;
        _cfg.Shield.RequireTextMs = _cfg.Life.RequireTextMs;

        _cfg.Save();
        _engine.SyncTextRegions();

        // Telling it your maximum is the whole basis of the memory search, so
        // changing it should start a new one rather than wait to be asked.
        if (_cfg.Life.KnownMax != _lastKnownLife || _cfg.Mana.KnownMax != _lastKnownMana)
        {
            _lastKnownLife = _cfg.Life.KnownMax;
            _lastKnownMana = _cfg.Mana.KnownMax;
            if (_cfg.UseMemory)
            {
                Log.Write($"memory: max changed to life {_lastKnownLife}, "
                          + $"mana {_lastKnownMana} - searching again");
                _engine.RescanMemory();
            }
        }

        RefreshArmUi();
    }

    /// <summary>Shows or hides the small always-on-top readout.</summary>
    private void ToggleOverlay(bool on)
    {
        if (on)
        {
            if (_overlay is null || _overlay.IsDisposed)
            {
                _overlay = new OverlayForm();
                // Remember where it was dropped, so it comes back there rather
                // than only being saved when the app closes.
                _overlay.Moved += () =>
                {
                    if (_overlay is not { IsDisposed: false }) return;
                    _cfg.OverlayX = _overlay.Location.X;
                    _cfg.OverlayY = _overlay.Location.Y;
                    _cfg.Save();
                };
            }

            _overlay.Locked = _cfg.OverlaySnap;
            PlaceOverlay();
            _overlay.SetArmed(_engine.Armed);
            _overlay.Show();
            _overlay.BringToFront();
        }
        else
        {
            if (_overlay is { IsDisposed: false })
            {
                _cfg.OverlayX = _overlay.Location.X;
                _cfg.OverlayY = _overlay.Location.Y;
                _overlay.Hide();
            }
        }

        _cfg.OverlayOn = on;
        _pin.BackColor = on ? Color.FromArgb(200, 60, 60) : SystemColors.Control;
        _pin.ForeColor = on ? Color.White : SystemColors.ControlText;
        Save();
    }

    /// <summary>
    /// Puts the overlay where it was left, unless that is somewhere it cannot
    /// be seen. A window dragged mostly off a screen, or onto a monitor that is
    /// no longer there, is indistinguishable from one that has vanished.
    /// </summary>
    private void PlaceOverlay(bool forceDefault = false)
    {
        if (_overlay is null || _overlay.IsDisposed) return;

        // Snapped, it sits directly above the game's own life numbers. That box
        // is a fixed part of the HUD and already tracked, so it follows the
        // readout it is describing rather than a remembered screen position
        // that is wrong the moment a window moves or a monitor changes.
        if (!forceDefault && _cfg.OverlaySnap && _cfg.Life.TextRegion.IsValid)
        {
            var box = _cfg.Life.TextRegion.ToRect();
            var at = new Point(box.X, box.Y - _overlay.Height - 6);
            if (at.Y < 0) at.Y = box.Bottom + 6;
            _overlay.Location = at;
            return;
        }

        var wanted = new Rectangle(_cfg.OverlayX, _cfg.OverlayY,
                                   _overlay.Width, _overlay.Height);

        bool visible = !forceDefault && _cfg.OverlayX >= 0 && _cfg.OverlayY >= 0
            && Screen.AllScreens.Any(sc =>
            {
                var shown = Rectangle.Intersect(sc.WorkingArea, wanted);
                // Most of it has to be on a screen, not merely a corner.
                return shown.Width >= wanted.Width / 2 && shown.Height >= wanted.Height / 2;
            });

        _overlay.Location = visible
            ? new Point(_cfg.OverlayX, _cfg.OverlayY)
            : new Point(Screen.PrimaryScreen!.WorkingArea.Right - _overlay.Width - 20, 20);

        if (!visible)
        {
            _cfg.OverlayX = _overlay.Location.X;
            _cfg.OverlayY = _overlay.Location.Y;
        }
    }

    /// <summary>Brings the overlay back to a known spot, however it was lost.</summary>
    private void ResetOverlay()
    {
        ToggleOverlay(true);
        PlaceOverlay(forceDefault: true);
        _overlay?.BringToFront();
        Save();
    }

    private void OnSampled(GlobeReading life, GlobeReading mana, GlobeReading shield,
                           bool focused)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_overlay is { IsDisposed: false })
                {
                    // Nothing true to say while a shop or the passive tree
                    // covers the HUD, and nothing can fire either - so getting
                    // out of the way says something rather than hides it.
                    bool blind = life.Note == "numbers not on screen";
                    bool wanted = !_cfg.OverlayAutoHide || (!blind && focused);

                    if (_cfg.OverlayOn && wanted != _overlay.Visible)
                    {
                        if (wanted) _overlay.Show(); else _overlay.Hide();
                    }

                    if (_overlay.Visible)
                    {
                        _overlay.Show(life, mana, _cfg.Life.Threshold, _cfg.Mana.Threshold);
                        _overlay.SetFightCount(_engine.FiresThisFight, _engine.InCombat);
                        if (_cfg.OverlaySnap) PlaceOverlay();
                    }
                }

                _life.Update(life);
                _mana.Update(mana);

                _life.UpdateShield(shield);

                // Memory sits idle until both maxima are known, and the panel
                // that is missing one is the place to say so.
                bool waiting = _cfg.UseMemory && !_engine.MemoryFound;
                _life.NeedMaxForMemory(waiting);
                _mana.NeedMaxForMemory(waiting);
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
                    : $"focused window: \"{_engine.ForegroundTitle}\"   "
                      + $"polls/sec: {_engine.ActualHz}   "
                      + $"work per poll: {_engine.LastPollMs:0.0} ms   "
                      + $"memory: {_engine.MemoryStatus}";
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

        // The per-globe line describes the last press, or the last time it
        // would have pressed. Arming or disarming makes either one stale.
        _life.ClearStatus();
        _mana.ClearStatus();
        if (_overlay is { IsDisposed: false }) _overlay.SetArmed(on);
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
        menu.Items.Add("Bring overlay back", null, (_, _) => ResetOverlay());
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

    /// <summary>
    /// Says which settings this update changed, and why.
    ///
    /// Changing settings someone chose deliberately is only acceptable if they
    /// are told. It runs from OnShown rather than the constructor: there is no
    /// window handle to show a dialog over until the form is up.
    /// </summary>
    private void ReportRepairs()
    {
        if (_cfg.Repairs.Count == 0) return;
        MessageBox.Show(this,
            "Some settings were changed by this update:"
            + Environment.NewLine + Environment.NewLine
            + " - " + string.Join(Environment.NewLine + Environment.NewLine + " - ",
                                  _cfg.Repairs),
            "Settings updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _cfg.Repairs.Clear();
    }

    /// <summary>
    /// Sets the numbers up on its own the first time, if they are not set and
    /// the game is there to look at. It is one button, but it is also the one
    /// step everything else depends on, and leaving it to be discovered means
    /// running on the globe pixels - which cannot tell life from shield and
    /// read a poisoned globe as empty.
    /// </summary>
    private void FirstRunSetup()
    {
        if (!_engine.TextAvailable) return;
        if (_cfg.Life.TextRegion.IsValid || _cfg.Mana.TextRegion.IsValid) return;
        if (Native.FindWindowRect(_cfg.WindowMatch) is null) return;

        Log.Write("first run: no numbers set and the game is up - finding them");
        string result = _engine.FindAllNumbers();
        Save();
        _life.RefreshFromConfig();
        _mana.RefreshFromConfig();

        MessageBox.Show(this,
            "The numbers were not set up yet, so they have been found for you:"
            + Environment.NewLine + Environment.NewLine + result
            + Environment.NewLine + Environment.NewLine
            + "These are exact, need no calibration, and stop it acting on menu "
            + "screens. Your maximums fill in from them on their own.",
            "Set up", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ReportRepairs();
        FirstRunSetup();
        // With unattended installs on, the launch check has nothing to ask
        // about: the countdown below handles it a few seconds later, in one
        // place, and having waited for a fight to end.
        if (_cfg.CheckUpdatesOnStart)
            _ = Updater.CheckAsync(this, silent: true, beforeExit: _cfg.SaveNow,
                                   ui: !_cfg.AutoInstall);
        if (_cfg.ResumeArmed)
        {
            _cfg.ResumeArmed = false;
            _cfg.SaveNow();
            Log.Write("armed again after an update restart");
            _engine.Toggle();
        }

        _critical.Start();
        _watch.Start();
        if (_cfg.StartMinimised) Hide();
    }

    /// <summary>
    /// Counts a critical update down in the overlay and then stops the session.
    ///
    /// A message box behind a fullscreen game is not a warning, it is a thing
    /// discovered afterwards - and the releases this is for are the ones that
    /// were letting somebody die while the app watched. Thirty seconds of
    /// notice on the overlay, then Escape into the game's own menu so play
    /// actually stops, and this window brought forward with the update ready.
    ///
    /// Never mid-fight. Pausing someone who is being hit is its own way of
    /// getting them killed, so the countdown holds at zero until they are out
    /// of combat.
    /// </summary>
    private void OnCriticalTick()
    {
        if (_criticalDone) return;

        // Two kinds of interruption, the same countdown. A critical release
        // stops the game and says why, because staying behind on it is how
        // somebody dies. An ordinary one, only a release or three ahead, is
        // small enough to simply take: notice on the overlay, then it swaps
        // itself and comes back. Both wait for the fight to end first.
        bool critical = Updater.Critical is not null;
        bool ordinary = !critical && _cfg.AutoInstall && Updater.Pending is not null
                        && Updater.Behind <= _cfg.AutoInstallMaxBehind;

        if (!critical && !ordinary)
        {
            _criticalLeft = 0;
            return;
        }

        // A fresh countdown from whichever kind was found. Thirty seconds to
        // stop and read something that can kill you; ten to notice a restart
        // that takes a couple of seconds and puts everything back.
        if (_criticalLeft <= 0) _criticalLeft = critical ? 31 : 11;

        _criticalLeft--;

        string what = Updater.Critical ?? Updater.Pending ?? "";

        if (_criticalLeft > 0)
        {
            _overlay?.SetAlert(critical
                ? $"UPDATE {what} - pausing in {_criticalLeft}s"
                : $"UPDATE {what} - restarting in {_criticalLeft}s");
            return;
        }

        if (_engine.InCombat)
        {
            _overlay?.SetAlert($"UPDATE {what} - waiting until you are safe");
            return;
        }

        _criticalDone = true;
        _critical.Stop();
        _overlay?.SetAlert("");

        // Come back the way it was left. Only here - every ordinary launch
        // starts disarmed on purpose.
        _cfg.ResumeArmed = _engine.Armed;
        _cfg.SaveNow();

        if (!critical)
        {
            Log.Write($"update: taking {what} unattended, armed={_cfg.ResumeArmed}");
            _ = Updater.CheckAsync(this, silent: true, beforeExit: _cfg.SaveNow,
                                   ui: false, install: true);
            return;
        }

        Log.Write($"update: pausing the session for critical release {Updater.Critical}");

        if (_engine.GameWindow != 0)
        {
            KeySender.PostTo(_engine.GameWindow, "Escape", 70);
            KeySender.Tap("Escape", 70);
        }

        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();

        // Activate() asks politely, and Windows refuses a background process
        // the foreground - it flashes the taskbar instead, which behind a
        // fullscreen game is nothing at all. Take it properly, and sit above
        // the game until the warning has been read.
        Native.ForceForeground(Handle);
        bool wasTop = TopMost;
        TopMost = true;

        MessageBox.Show(this,
            $"{Updater.Critical} fixes something that can get you killed:"
            + Environment.NewLine + Environment.NewLine
            + Updater.CriticalWhy
            + Environment.NewLine + Environment.NewLine
            + "Your game has been paused. It will update itself now and be back "
            + "in a moment, still armed if it was armed.",
            "Critical update", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        TopMost = wasTop;

        // It installs itself from here. A release that fixes a way to die
        // quietly is the one that most needs installing, not the one that most
        // needs a second dialog - and the previous one was leaving people
        // paused, warned, and still on the broken version.
        _ = Updater.CheckAsync(this, silent: true, beforeExit: _cfg.SaveNow,
                               ui: false, install: true);
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
    /// <summary>
    /// One button for the whole setup: hide, look at the game, find every stat
    /// line by its label, and point the watchers at them.
    /// </summary>
    private void FindAllNumbers()
    {
        Hide();
        Thread.Sleep(350);
        string result;
        try { result = _engine.FindAllNumbers(); }
        finally { Show(); }

        Save();
        _life.RefreshFromConfig();
        _mana.RefreshFromConfig();

        bool trouble = result.Contains("WARNING") || result.Contains("could not")
                       || result.Contains("Could not");
        MessageBox.Show(this, result, "Find numbers", MessageBoxButtons.OK,
                        trouble ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

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
        if (_overlay is { IsDisposed: false })
        {
            _cfg.OverlayX = _overlay.Location.X;
            _cfg.OverlayY = _overlay.Location.Y;
            _overlay.Dispose();
        }
        _cfg.SaveNow();
        if (_hotkeyRegistered) Native.UnregisterHotKey(Handle, HotkeyId);
        _tray.Visible = false;
        base.OnFormClosing(e);
    }
}
