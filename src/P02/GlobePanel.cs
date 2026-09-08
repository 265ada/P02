namespace P02;

/// <summary>Controls for one globe: on/off, region, trigger point, key.</summary>
public sealed class GlobePanel : GroupBox
{
    private readonly WatcherConfig _cfg;
    private readonly bool _blue;
    private readonly Action _onChange;
    private readonly Func<string> _windowMatch;
    private readonly Func<Box?> _other;

    private readonly CheckBox _enabled = new();
    private readonly Label _region = new();
    private readonly NumericUpDown _threshold = new();
    private readonly NumericUpDown _cooldown = new();
    private readonly NumericUpDown _panicBelow = new();
    private readonly NumericUpDown _panicGap = new();
    private readonly NumericUpDown _burst = new();
    private readonly Label _tuned = new();
    private readonly Label _warn = new();
    private readonly KeyBindBox _key = new();
    private readonly LevelBar _bar = new();
    private readonly Label _pct = new();

    public GlobePanel(string title, WatcherConfig cfg, bool blue, Action onChange,
                      Func<string> windowMatch, Func<Box?> otherRegion)
    {
        _cfg = cfg;
        _blue = blue;
        _onChange = onChange;
        _windowMatch = windowMatch;
        _other = otherRegion;

        Text = title;
        Width = 366;
        Height = 398;
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
            { _cfg.BurstCount = (int)_burst.Value; _onChange(); };
        Controls.Add(_burst);

        Controls.Add(new Label
        {
            Bounds = new Rectangle(196, y + 4, 120, 18),
            ForeColor = SystemColors.GrayText,
            Text = "charges allowing",
        });
    }

    /// <summary>
    /// Flags settings that let a drained globe read as full - the failure that
    /// looks like nothing at all until it costs you a character.
    /// </summary>
    private void RefreshWarning()
    {
        if (!_cfg.Region.IsValid) { _warn.Text = ""; return; }

        if (_cfg.ColourMargin < 10 && _cfg.EmptyDominance < 0)
            _warn.Text = "Colour margin is very low. An empty globe may read as full. "
                       + "Press Empty = 0% while drained.";
        else if (_cfg.EmptyDominance < 0)
            _warn.Text = "Not calibrated against an empty globe. If it never fires, "
                       + "press Empty = 0% while drained.";
        else
            _warn.Text = "";
    }

    private static Label Lab(string text, int x, int y) =>
        new() { Text = text, Bounds = new Rectangle(x, y, 76, 18), AutoSize = true };

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
        if (CalibrateFullSilently(out string note))
            MessageBox.Show(this,
                "Found it and calibrated against the full globe." + Environment.NewLine
                + note + Environment.NewLine + Environment.NewLine
                + "Now spend this globe down and press Empty = 0% to finish.",
                "Auto-find", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Preview();
    }

    /// <summary>
    /// The measuring half of Full = 100%, with no prompts. Returns false if no
    /// liquid could be seen in the region.
    /// </summary>
    private bool CalibrateFullSilently(out string note)
    {
        note = "";
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

        double reads = OrbDetector.Fraction(buf, shot.Width, shot.Height, _cfg);
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
    }
}
