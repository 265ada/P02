namespace P02;

/// <summary>Controls for one globe: on/off, region, trigger point, key.</summary>
public sealed class GlobePanel : GroupBox
{
    private readonly WatcherConfig _cfg;
    private readonly bool _blue;
    private readonly Action _onChange;
    private readonly Func<string> _windowMatch;
    private readonly Func<Box?> _other;
    private readonly TextProbe _probe;

    private readonly CheckBox _enabled = new();
    private readonly Label _region = new();
    private readonly NumericUpDown _threshold = new();
    private readonly NumericUpDown _cooldown = new();
    private readonly NumericUpDown _panicBelow = new();
    private readonly NumericUpDown _panicGap = new();
    private readonly NumericUpDown _burst = new();
    private readonly NumericUpDown _hold = new();
    private readonly NumericUpDown _knownMax = new();
    private readonly Label _burstTime = new();
    private readonly Label _effect = new();
    private bool _blind;
    private readonly Label _numbers = new();

    // Only the life panel carries these: energy shield is recovered by the life
    // flask, when it is recovered at all.
    private readonly WatcherConfig? _shield;
    private readonly CheckBox _shieldOn = new();
    private readonly NumericUpDown _shieldBelow = new();
    private readonly NumericUpDown _shieldMax = new();
    private readonly Label _shieldRead = new();
    private readonly Label _tuned = new();
    private readonly Label _warn = new();
    private readonly KeyBindBox _key = new();
    private readonly LevelBar _bar = new();
    private readonly Label _pct = new();

    public GlobePanel(string title, WatcherConfig cfg, bool blue, Action onChange,
                      Func<string> windowMatch, Func<Box?> otherRegion, TextProbe probe,
                      WatcherConfig? shield = null)
    {
        _probe = probe;
        _shield = shield;
        _cfg = cfg;
        _blue = blue;
        _onChange = onChange;
        _windowMatch = windowMatch;
        _other = otherRegion;

        Text = title;
        Width = 382;
        Height = 492;
        Padding = new Padding(10);

        int y = 24;

        _enabled.Text = "Watch this globe";
        _enabled.Checked = cfg.Enabled;
        _enabled.Font = new Font(Font, FontStyle.Bold);
        _enabled.SetBounds(14, y, 200, 24);
        _enabled.CheckedChanged += (_, _) => { _cfg.Enabled = _enabled.Checked; _onChange(); };
        Controls.Add(_enabled);
        y += 30;

        _bar.SetBounds(14, y, 200, 18);
        Controls.Add(_bar);
        _pct.SetBounds(222, y, 90, 18);
        _pct.Text = "--";
        Controls.Add(_pct);
        y += 28;

        Controls.Add(Lab("Region", 14, y + 4));
        _region.SetBounds(70, y + 4, 150, 18);
        _region.Text = cfg.Region.ToString();
        Controls.Add(_region);
        y += 26;

        var setBtn = new Button { Text = "Set…", Bounds = new Rectangle(70, y, 56, 26) };
        setBtn.Click += (_, _) => PickRegion();
        Controls.Add(setBtn);

        var autoBtn = new Button { Text = "Auto-find", Bounds = new Rectangle(130, y, 72, 26) };
        autoBtn.Click += (_, _) => AutoFind();
        Controls.Add(autoBtn);

        var calBtn = new Button { Text = "Full = 100%", Bounds = new Rectangle(206, y, 86, 26) };
        calBtn.Click += (_, _) => CalibrateFull();
        Controls.Add(calBtn);

        var prevBtn = new Button { Text = "Check", Bounds = new Rectangle(296, y, 56, 26) };
        prevBtn.Click += (_, _) => Preview();
        Controls.Add(prevBtn);
        y += 30;

        var numBtn = new Button { Text = "Numbers...", Bounds = new Rectangle(296, y, 76, 26) };
        numBtn.Click += (_, _) => PickTextRegion();
        Controls.Add(numBtn);

        var emptyBtn = new Button { Text = "Empty = 0%", Bounds = new Rectangle(206, y, 86, 26) };
        emptyBtn.Click += (_, _) => CalibrateEmpty();
        Controls.Add(emptyBtn);

        _tuned.SetBounds(14, y + 5, 188, 18);
        _tuned.ForeColor = SystemColors.GrayText;
        Controls.Add(_tuned);
        y += 34;

        _warn.SetBounds(14, y, 338, 32);
        _warn.ForeColor = Color.FromArgb(190, 60, 0);
        Controls.Add(_warn);
        y += 34;

        Controls.Add(Lab("Fire below", 14, y + 4));
        _threshold.SetBounds(90, y, 62, 24);
        _threshold.Minimum = 1;
        _threshold.Maximum = 99;
        _threshold.Value = (decimal)Math.Clamp(cfg.Threshold * 100, 1, 99);
        _threshold.ValueChanged += (_, _) =>
            { _cfg.Threshold = (double)_threshold.Value / 100.0; _onChange(); };
        Controls.Add(_threshold);
        Controls.Add(Lab("%", 156, y + 4));

        Controls.Add(Lab("Key", 190, y + 4));
        _key.SetBounds(222, y, 84, 24);
        _key.Key = cfg.Key;
        _key.KeyBound += k => { _cfg.Key = k; _onChange(); };
        Controls.Add(_key);
        y += 32;

        Controls.Add(Lab("Cooldown", 14, y + 4));
        _cooldown.SetBounds(90, y, 76, 24);
        _cooldown.Minimum = 20;
        _cooldown.Maximum = 60000;
        _cooldown.Increment = 20;
        _cooldown.Value = Math.Clamp(cfg.CooldownMs, 20, 60000);
        _cooldown.ValueChanged += (_, _) =>
            { _cfg.CooldownMs = (int)_cooldown.Value; _onChange(); };
        Controls.Add(_cooldown);
        Controls.Add(Lab("ms", 170, y + 4));
        y += 32;

        Controls.Add(Lab("Panic below", 14, y + 4));
        _panicBelow.SetBounds(90, y, 62, 24);
        _panicBelow.Minimum = 0;
        _panicBelow.Maximum = 99;
        _panicBelow.Value = (decimal)Math.Clamp(cfg.PanicBelow * 100, 0, 99);
        _panicBelow.ValueChanged += (_, _) =>
            { _cfg.PanicBelow = (double)_panicBelow.Value / 100.0; _onChange(); };
        Controls.Add(_panicBelow);
        Controls.Add(Lab("%", 156, y + 4));

        Controls.Add(Lab("gap", 190, y + 4));
        _panicGap.SetBounds(222, y, 84, 24);
        _panicGap.Minimum = 10;
        _panicGap.Maximum = 5000;
        _panicGap.Increment = 10;
        _panicGap.Value = Math.Clamp(cfg.PanicCooldownMs, 10, 5000);
        _panicGap.ValueChanged += (_, _) =>
            { _cfg.PanicCooldownMs = (int)_panicGap.Value; _onChange(); };
        Controls.Add(_panicGap);
        y += 32;

        Controls.Add(Lab("Presses per trigger", 14, y + 4));
        _burst.SetBounds(140, y, 50, 24);
        _burst.Minimum = 1;
        _burst.Maximum = 5;
        _burst.Value = Math.Clamp(cfg.BurstCount, 1, 5);
        _burst.ValueChanged += (_, _) =>
            { _cfg.BurstCount = (int)_burst.Value; RefreshBurstTime(); _onChange(); };
        Controls.Add(_burst);

        Controls.Add(new Label
        {
            Bounds = new Rectangle(196, y + 4, 120, 18),
            ForeColor = SystemColors.GrayText,
            Text = "charges allowing",
        });
        y += 32;

        Controls.Add(Lab("Hold each press", 14, y + 4));
        _hold.SetBounds(140, y, 60, 24);
        _hold.Minimum = 10;
        _hold.Maximum = 400;
        _hold.Increment = 10;
        _hold.Value = Math.Clamp(cfg.HoldMs, 10, 400);
        _hold.ValueChanged += (_, _) =>
        { _cfg.HoldMs = (int)_hold.Value; RefreshBurstTime(); _onChange(); };
        Controls.Add(_hold);
        Controls.Add(Lab("ms", 204, y + 4));

        Controls.Add(new Label
        {
            Bounds = new Rectangle(232, y + 4, 130, 18),
            ForeColor = SystemColors.GrayText,
            Text = "raise if presses are missed",
        });
        y += 30;

        Controls.Add(Lab("My max", 14, y + 4));
        _knownMax.SetBounds(140, y, 72, 24);
        _knownMax.Minimum = 0;
        _knownMax.Maximum = 1_000_000;
        _knownMax.Increment = 1;
        _knownMax.Value = Math.Clamp(cfg.KnownMax, 0, 1_000_000);
        _knownMax.ValueChanged += (_, _) => { _cfg.KnownMax = (int)_knownMax.Value; _onChange(); };
        Controls.Add(_knownMax);
        Controls.Add(new Label
        {
            Bounds = new Rectangle(218, y + 4, 150, 18),
            ForeColor = SystemColors.GrayText,
            Text = "0 = work it out",
        });
        y += 26;

        if (_shield is not null) y = AddShield(y);

        // Hold time and press count multiply out into how long a trigger takes
        // to send, and nothing else can go out during it. Worth seeing.
        _burstTime.SetBounds(14, y, 340, 18);
        _burstTime.ForeColor = SystemColors.GrayText;
        Controls.Add(_burstTime);
        RefreshBurstTime();
        y += 22;

        _effect.SetBounds(14, y, 340, 18);
        _effect.ForeColor = SystemColors.GrayText;
        Controls.Add(_effect);
        y += 20;

        _numbers.SetBounds(14, y, 340, 18);
        _numbers.ForeColor = SystemColors.GrayText;
        _numbers.Text = "Deciding: not read yet";
        Controls.Add(_numbers);
    }

    /// <summary>
    /// Flags settings that let a drained globe read as full - the failure that
    /// looks like nothing at all until it costs you a character.
    /// </summary>
    /// <summary>
    /// Shows how long one trigger takes to send. Nothing else can be sent
    /// during it, so this is the real floor on how often it can act - the
    /// cooldown cannot go below it however low it is set.
    /// </summary>
    private void RefreshBurstTime()
    {
        int n = Math.Max(1, _cfg.BurstCount);
        int ms = n * _cfg.HoldMs + (n - 1) * _cfg.BurstGapMs;
        _burstTime.Text = n == 1
            ? $"One press takes {ms} ms to send, so at most {1000 / Math.Max(1, ms)} a second."
            : $"{n} presses take {ms} ms to send, so at most "
              + $"{1000 / Math.Max(1, ms)} bursts a second.";
    }

    private void RefreshWarning()
    {
        if (_blind) return;
        if (!_cfg.Region.IsValid) { _warn.Text = ""; return; }

        if (!_cfg.TextRegion.IsValid && _probe.Available)
            _warn.Text = "No numbers set. Press Numbers... - it is exact, needs no "
                       + "calibration, and stops it acting on menu screens.";
        else if (_cfg.ColourMargin < 10 && _cfg.EmptyDominance < 0)
            _warn.Text = "Colour margin is very low. An empty globe may read as full. "
                       + "Press Empty = 0% while drained.";
        else if (_cfg.EmptyDominance < 0)
            _warn.Text = "Not calibrated against an empty globe. If it never fires, "
                       + "press Empty = 0% while drained.";
        else
            _warn.Text = "";
    }

    /// <summary>
    /// Energy shield, sharing this globe's key and timing because the life
    /// flask is what recovers it - and only for characters who have taken
    /// something that makes it do so.
    /// </summary>
    private int AddShield(int y)
    {
        Controls.Add(new Label
        {
            Bounds = new Rectangle(14, y, 352, 2),
            BorderStyle = BorderStyle.Fixed3D,
        });
        y += 10;

        _shieldOn.Text = "Also fire for energy shield";
        _shieldOn.Checked = _shield!.Enabled;
        _shieldOn.SetBounds(14, y, 190, 22);
        _shieldOn.CheckedChanged += (_, _) =>
        { _shield.Enabled = _shieldOn.Checked; _onChange(); };
        Controls.Add(_shieldOn);

        Controls.Add(Lab("below", 208, y + 3));
        _shieldBelow.SetBounds(250, y, 54, 24);
        _shieldBelow.Minimum = 1;
        _shieldBelow.Maximum = 99;
        _shieldBelow.Value = (decimal)Math.Clamp(_shield.Threshold * 100, 1, 99);
        _shieldBelow.ValueChanged += (_, _) =>
        { _shield.Threshold = (double)_shieldBelow.Value / 100.0; _onChange(); };
        Controls.Add(_shieldBelow);
        Controls.Add(Lab("%", 308, y + 3));
        y += 28;

        var numBtn = new Button { Text = "Numbers...", Bounds = new Rectangle(14, y, 80, 24) };
        numBtn.Click += (_, _) => PickShieldNumbers();
        Controls.Add(numBtn);

        Controls.Add(Lab("My max", 102, y + 3));
        _shieldMax.SetBounds(160, y, 72, 24);
        _shieldMax.Minimum = 0;
        _shieldMax.Maximum = 1_000_000;
        _shieldMax.Value = Math.Clamp(_shield.KnownMax, 0, 1_000_000);
        _shieldMax.ValueChanged += (_, _) =>
        { _shield.KnownMax = (int)_shieldMax.Value; _onChange(); };
        Controls.Add(_shieldMax);
        y += 26;

        _shieldRead.SetBounds(14, y, 352, 18);
        _shieldRead.ForeColor = SystemColors.GrayText;
        _shieldRead.Text = "Shield: not set - needs its own Numbers box";
        Controls.Add(_shieldRead);
        return y + 22;
    }

    private void PickShieldNumbers()
    {
        if (!_probe.Available)
        {
            MessageBox.Show(this, "Windows OCR is not available, so the shield numbers "
                            + "cannot be read.", "Energy shield",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var owner = FindForm();
        owner?.Hide();
        Thread.Sleep(180);
        var r = RegionPickerForm.Pick(
            "Drag a box around the Shield numbers - include the word \"Shield\"");
        owner?.Show();
        if (r is null) return;

        string got = _probe.Probe(r.Value);
        if (got.Length == 0)
        {
            MessageBox.Show(this, "Nothing readable in that box.", "Energy shield",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _shield!.TextRegion = Box.From(r.Value);
        _shield.UseText = true;
        _onChange();
        MessageBox.Show(this, $"Read: \"{got}\"", "Energy shield",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Latest energy shield reading, shown under the life controls.</summary>
    public void UpdateShield(GlobeReading r)
    {
        if (_shield is null) return;
        if (!_shield.Enabled)
        {
            _shieldRead.Text = "Shield: not watched";
            _shieldRead.ForeColor = SystemColors.GrayText;
            return;
        }
        if (!r.Ok || !r.FromText)
        {
            _shieldRead.Text = "Shield: no reading - set its Numbers box";
            _shieldRead.ForeColor = Color.FromArgb(190, 60, 0);
            return;
        }
        _shieldRead.Text = $"Shield: {r.TextRaw}  ({r.Fraction * 100:0} %)";
        _shieldRead.ForeColor = r.Fraction < _shield.Threshold
            ? Color.FromArgb(190, 60, 0)
            : Color.FromArgb(0, 100, 0);
    }

    private static Label Lab(string text, int x, int y) =>
        new() { Text = text, Bounds = new Rectangle(x, y, 76, 18), AutoSize = true };

    /// <summary>
    /// The numbers beside the globe are an exact reading, so this is the most
    /// reliable thing to set: no calibration, no colour thresholds, and the
    /// maximum is read too, so gear that changes your pool does not matter.
    /// </summary>
    private void PickTextRegion()
    {
        if (!_probe.Available)
        {
            MessageBox.Show(this,
                "Windows OCR is not available on this machine, so the numbers cannot be "
                + "read. The globe pixels will be used instead."
                + Environment.NewLine + Environment.NewLine + _probe.Reason,
                "Numbers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var owner = FindForm();
        owner?.Hide();
        Thread.Sleep(180);
        var r = RegionPickerForm.Pick(
            $"Drag a box around the {Text} numbers - include the word "
            + $"\"{Text}\" so shield and ward cannot be mistaken for it");
        owner?.Show();
        if (r is null) return;

        string got = _probe.Probe(r.Value);
        if (got.Length == 0)
        {
            MessageBox.Show(this,
                "Nothing readable in that box. Include the whole \"1,465/1,465\" and a "
                + "little space around it, and try not to catch the label.",
                "Numbers", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cfg.TextRegion = Box.From(r.Value);
        _cfg.UseText = true;
        _numbers.Text = $"Numbers: read \"{got}\"";
        _onChange();

        MessageBox.Show(this,
            $"Read: \"{got}\"" + Environment.NewLine + Environment.NewLine
            + "This is now what decides when to fire, and it needs no calibration. "
            + "The globe pixels stay as the fallback.",
            "Numbers", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PickRegion()
    {
        var owner = FindForm();
        owner?.Hide();
        Thread.Sleep(180);           // let the window actually disappear first
        var r = RegionPickerForm.Pick($"Drag a box around the {Text} globe");
        owner?.Show();
        if (r is null) return;
        Apply(r.Value);
    }

    private void AutoFind()
    {
        if (MessageBox.Show(
                $"Auto-find looks for the {Text.ToLowerInvariant()} globe by colour, so it only " +
                "works when the globe is FULL.\n\nMake sure the game is on screen and the " +
                $"{Text.ToLowerInvariant()} globe is topped up, then press OK. " +
                "The window hides for a moment while it looks.",
                "Auto-find", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information) != DialogResult.OK)
            return;

        var owner = FindForm();
        owner?.Hide();
        Thread.Sleep(400);

        // The globes are in the corners of the GAME, which on a multi-monitor
        // desktop is not the corner of the virtual screen. Searching the whole
        // virtual desktop is why the bottom-right search used to land on a
        // second monitor and never find the mana globe at all.
        var area = GameArea();
        int w = (int)(area.Width * 0.25);
        int h = (int)(area.Height * 0.36);
        var search = _blue
            ? new Rectangle(area.Right - w, area.Bottom - h, w, h)
            : new Rectangle(area.Left, area.Bottom - h, w, h);

        // Deliberately not the detection settings: auto-find has shape checks
        // to fall back on, and coupling the two led to advice that lowered the
        // detection margin until an empty globe read as full.
        var found = OrbDetector.AutoLocate(search, _blue, margin: 18, minV: 45);
        owner?.Show();

        if (found is null)
        {
            string why = OrbDetector.LastLocateNote;
            string msg = "Couldn't find it.";
            msg += Environment.NewLine + Environment.NewLine +
                   $"Looked in {search.Width}x{search.Height} at {search.X},{search.Y} " +
                   $"(the {(_blue ? "bottom-right" : "bottom-left")} of {area.Width}x{area.Height} " +
                   $"at {area.X},{area.Y}).";
            if (why.Length > 0) msg += Environment.NewLine + Environment.NewLine + why;
            msg += Environment.NewLine + Environment.NewLine +
                   "Use Set... and drag the box by hand - that always works, and the "
                   + "Check window will confirm it reads correctly.";
            MessageBox.Show(msg, "Auto-find", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Apply(found.Value);

        // Auto-find already requires a full globe, and its box comes from the
        // edges of the colour blob, which sit a few percent inside the real
        // liquid. Calibrating straight away off the same full globe is what
        // makes a full globe read exactly 100% instead of 88-96%.
        bool calibrated = CalibrateFullSilently(out string note, out double reads);

        // A box on the globe reads full right after calibrating against it. One
        // that does not is on something else, or on a fraction of the globe.
        if (calibrated && reads >= 0.95)
        {
            MessageBox.Show(this,
                "Found it and calibrated against the full globe." + Environment.NewLine
                + note + Environment.NewLine + Environment.NewLine
                + "Now spend this globe down and press Empty = 0% to finish.",
                "Auto-find", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this,
                $"Found a {found.Value.Width}x{found.Value.Height} region, but it does not "
                + "look right: after calibrating against it, a full globe reads "
                + $"{reads:P0} rather than 100%." + Environment.NewLine + Environment.NewLine
                + "That usually means the box is on part of the globe rather than all of it, "
                + "or on something else entirely. Press Check to see what it is looking at, "
                + "and use Set... to drag the box yourself if it is wrong.",
                "Auto-find", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Preview();
    }

    /// <summary>
    /// The measuring half of Full = 100%, with no prompts. Returns false if no
    /// liquid could be seen in the region.
    /// </summary>
    private bool CalibrateFullSilently(out string note, out double reads)
    {
        note = "";
        reads = 0;
        if (!_cfg.Region.IsValid) return false;

        using var shot = ScreenCapture.Snapshot(_cfg.Region.ToRect());
        var buf = ScreenCapture.ToBuffer(shot);
        if (!OrbDetector.CalibrateFull(buf, shot.Width, shot.Height, _cfg,
                                       out int full, out int empty))
            return false;

        _cfg.FullRow = full;
        _cfg.EmptyRow = empty;

        var st = OrbDetector.Measure(buf, shot.Width, shot.Height, _cfg, full, empty);
        _cfg.FullDominance = st.DomLow;
        _cfg.FullValue = st.ValLow;

        reads = OrbDetector.Fraction(buf, shot.Width, shot.Height, _cfg);
        note = $"Full is rows {full}-{empty} of {shot.Height}; it now reads {reads:P0}.";
        RefreshWarning();
        _onChange();
        return true;
    }

    /// <summary>
    /// Where to look for globes: the game window if we can find it, else the
    /// screen the other globe was set on, else the primary screen. Never the
    /// whole virtual desktop, which spans monitors the game is not on.
    /// </summary>
    private Rectangle GameArea()
    {
        var byTitle = Native.FindWindowRect(_windowMatch());
        if (byTitle is { } g && g.Width > 400 && g.Height > 300)
            return g;

        var other = _other();
        if (other is { IsValid: true })
            return Screen.FromRectangle(other.ToRect()).Bounds;

        return (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
    }

    private void Apply(Rectangle r)
    {
        _cfg.Region = Box.From(r);
        // Rows are box-relative, so an old calibration means nothing now.
        _cfg.FullRow = -1;
        _cfg.EmptyRow = -1;
        _region.Text = _cfg.Region.ToString();
        _onChange();
    }

    /// <summary>
    /// Pins "full" to where the liquid actually is, instead of assuming the box
    /// top is the top of the globe. Any frame caught in the box otherwise makes
    /// a full globe read a few percent short, and no colour setting can fix it.
    /// </summary>
    private void CalibrateFull()
    {
        if (!_cfg.Region.IsValid)
        {
            MessageBox.Show("Set a region first.", "Full = 100%",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string ask = $"Make sure your {Text.ToLowerInvariant()} globe is completely full, "
                     + "then press OK." + Environment.NewLine + Environment.NewLine + 
                       "This records where the liquid sits when full, so the reading "
                     + "hits a true 100%.";
        if (MessageBox.Show(ask,
                "Full = 100%", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information) != DialogResult.OK)
            return;

        var owner = FindForm();
        owner?.Hide();
        Thread.Sleep(250);
        using var shot = ScreenCapture.Snapshot(_cfg.Region.ToRect());
        owner?.Show();

        var buf = ScreenCapture.ToBuffer(shot);
        if (!OrbDetector.CalibrateFull(buf, shot.Width, shot.Height, _cfg,
                                       out int full, out int empty))
        {
            MessageBox.Show(
                "Couldn't see any liquid in that box. Open Check and lower Colour margin " +
                "until the globe lights up green, then try again.",
                "Full = 100%", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cfg.FullRow = full;
        _cfg.EmptyRow = empty;
        RefreshWarning();

        // Remember what liquid looks like, so Empty = 0% has something to
        // compare against.
        var st = OrbDetector.Measure(buf, shot.Width, shot.Height, _cfg, full, empty);
        _cfg.FullDominance = st.DomLow;
        _cfg.FullValue = st.ValLow;
        TryTune();
        _onChange();
        string done = $"Calibrated: full at row {full}, bottom at row {empty} "
                    + $"of {shot.Height}.";
        if (full > 0)
            done += Environment.NewLine + Environment.NewLine +
                    $"The box has {full} rows of frame above the liquid; that is what "
                    + "was costing you the missing percent.";
        MessageBox.Show(done, "Full = 100%", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
        Preview();
    }

    private void Preview()
    {
        if (!_cfg.Region.IsValid)
        {
            MessageBox.Show("Set a region first.", "Check",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var owner = FindForm();
        // The preview is live and sits on top, so the main window gets out of
        // the way rather than covering the globe being watched.
        owner?.Hide();
        using (var dlg = new PreviewForm(_cfg.Region.ToRect(), _cfg, Text, _onChange))
            dlg.ShowDialog();
        owner?.Show();
    }

    /// <summary>
    /// Records what the globe looks like when drained. A hue test alone cannot
    /// separate full from empty on the life globe — the drained part is the
    /// same red, only darker — so the threshold has to be learned from both.
    /// </summary>
    private void CalibrateEmpty()
    {
        if (!_cfg.Region.IsValid || _cfg.FullRow < 0)
        {
            MessageBox.Show("Set a region and press Full = 100% first.", "Empty = 0%",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string ask = $"Spend your {Text.ToLowerInvariant()} down as low as you can — "
                   + "the lower the better, empty is ideal."
                   + Environment.NewLine + Environment.NewLine
                   + "Then press OK. This records what a drained globe looks like, so the "
                   + "threshold can go between full and empty instead of being guessed.";
        if (MessageBox.Show(ask, "Empty = 0%", MessageBoxButtons.OKCancel,
                            MessageBoxIcon.Information) != DialogResult.OK)
            return;

        var owner = FindForm();
        owner?.Hide();
        Thread.Sleep(250);
        using var shot = ScreenCapture.Snapshot(_cfg.Region.ToRect());
        owner?.Show();

        var buf = ScreenCapture.ToBuffer(shot);
        // Measure the same rows the liquid occupied when full; those are the
        // ones that are now drained.
        var st = OrbDetector.Measure(buf, shot.Width, shot.Height, _cfg,
                                     _cfg.FullRow, _cfg.EmptyRow);
        if (st.Count == 0)
        {
            MessageBox.Show("Couldn't read anything in that box.", "Empty = 0%",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cfg.EmptyDominance = st.DomHigh;
        _cfg.EmptyValue = st.ValHigh;
        RefreshWarning();

        if (!OrbDetector.AutoTune(_cfg, out string note))
        {
            MessageBox.Show(note, "Empty = 0%", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _tuned.Text = "tuning failed";
            _onChange();
            return;
        }

        // Prove it on the very frame just captured.
        double nowReads = OrbDetector.Fraction(buf, shot.Width, shot.Height, _cfg);
        _tuned.Text = note;
        _onChange();

        MessageBox.Show(
            $"Tuned: {note}." + Environment.NewLine + Environment.NewLine
            + $"That drained globe now reads {nowReads * 100:0.0}%."
            + Environment.NewLine + Environment.NewLine
            + (nowReads < 0.35
                ? "Open Check and watch it track as you spend."
                : "That is still high. Press Check, and if it stays near 100% while the "
                + "globe drains, redo Empty = 0% with the globe emptier."),
            "Empty = 0%", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Preview();
    }

    private void TryTune()
    {
        if (OrbDetector.AutoTune(_cfg, out string note)) _tuned.Text = note;
    }

    /// <summary>
    /// Reports whether the last press actually moved the globe. A run of
    /// presses that change nothing is the clearest signal there is that the
    /// key, the charges, or the input path is the problem rather than the
    /// detection.
    /// </summary>
    public void ShowEffect(bool worked, int noEffectStreak)
    {
        if (worked)
        {
            // Deliberately not "it worked": regen and leech raise the globe too.
            _effect.Text = "Last press: the globe jumped, which looks like a flask.";
            _effect.ForeColor = Color.FromArgb(0, 120, 0);
        }
        else if (noEffectStreak >= 3)
        {
            _effect.Text = $"Last {noEffectStreak} presses moved nothing at all. Check the "
                         + "key, your charges, and Hold each press.";
            _effect.ForeColor = Color.FromArgb(190, 60, 0);
        }
        else if (noEffectStreak > 0)
        {
            _effect.Text = $"Last press: the globe did not move ({noEffectStreak} in a row).";
            _effect.ForeColor = SystemColors.GrayText;
        }
        else
        {
            _effect.Text = "Last press: the globe rose a little - could be regen or leech.";
            _effect.ForeColor = SystemColors.GrayText;
        }
    }

    /// <summary>
    /// The box is reading nothing at all. This is almost always a region that
    /// is not on the globe, and it is silent in every other way: no firing, no
    /// error, indistinguishable from a globe that is simply full.
    /// </summary>
    public void ShowBlind(bool blind)
    {
        _blind = blind;
        if (blind)
        {
            _warn.Text = $"{Text} has read 0% for 8 seconds. If the globe is not empty, "
                       + "this box is not on it - press Set... and drag one around it.";
            _warn.ForeColor = Color.FromArgb(200, 30, 30);
            _warn.Font = new Font(_warn.Font, FontStyle.Bold);
        }
        else
        {
            _warn.Font = new Font(_warn.Font, FontStyle.Regular);
            RefreshWarning();
        }
    }

    /// <summary>
    /// Wipes the last press/would-fire line. It describes a moment, not a
    /// state, so it has to go when the state it was written under changes -
    /// otherwise "Disarmed: would have fired" sits there while armed.
    /// </summary>
    public void ClearStatus()
    {
        _effect.Text = "";
        _effect.ForeColor = SystemColors.GrayText;
    }

    /// <summary>Shown while disarmed, when the trigger point is crossed.</summary>
    public void ShowWouldFire(double frac)
    {
        _effect.Text = $"Would have fired at {frac:P0} - disarmed, so nothing was sent.";
        _effect.ForeColor = Color.FromArgb(0, 90, 160);
    }

    /// <summary>Called from the UI thread with the latest reading.</summary>
    public void Update(GlobeReading r)
    {
        if (_warn.Text.Length == 0 && _cfg.EmptyDominance < 0) RefreshWarning();

        if (!r.Ok)
        {
            _pct.Text = r.Note.Length > 0 ? r.Note : "--";
            _bar.Value = 0;
            _bar.Below = false;
            return;
        }
        _bar.Value = r.Fraction;
        _bar.Below = r.Fraction < _cfg.Threshold;
        _pct.Text = $"{r.Fraction * 100:0.0} %";

        if (r.FromText && r.TextRaw.Length > 0)
        {
            // Which source decided matters more than the number itself. The
            // globe pixels cannot tell life from energy shield - the shield is
            // drawn over the same globe - while the numbers and memory read the
            // life value specifically.
            _numbers.Text = $"Deciding: {r.TextRaw}";
            _numbers.ForeColor = Color.FromArgb(0, 100, 0);
        }
        else if (r.Note == "numbers not on screen")
        {
            _numbers.Text = "Numbers not on screen - holding fire until they are back";
            _numbers.ForeColor = Color.FromArgb(0, 90, 160);
        }
        else if (_cfg.TextRegion.IsValid)
        {
            _numbers.Text = "Deciding: globe pixels (numbers set but not readable)";
            _numbers.ForeColor = Color.FromArgb(190, 60, 0);
        }
        else
        {
            _numbers.Text = "Deciding: globe pixels - these follow energy shield too";
            _numbers.ForeColor = Color.FromArgb(190, 60, 0);
        }
    }
}
