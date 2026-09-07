namespace P02;

/// <summary>Controls for one globe: on/off, region, trigger point, key.</summary>
public sealed class GlobePanel : GroupBox
{
    private readonly WatcherConfig _cfg;
    private readonly bool _blue;
    private readonly Action _onChange;

    private readonly CheckBox _enabled = new();
    private readonly Label _region = new();
    private readonly NumericUpDown _threshold = new();
    private readonly NumericUpDown _cooldown = new();
    private readonly KeyBindBox _key = new();
    private readonly ProgressBar _bar = new();
    private readonly Label _pct = new();

    public GlobePanel(string title, WatcherConfig cfg, bool blue, Action onChange)
    {
        _cfg = cfg;
        _blue = blue;
        _onChange = onChange;

        Text = title;
        Width = 330;
        Height = 250;
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
        _bar.Maximum = 1000;
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

        var setBtn = new Button { Text = "Set…", Bounds = new Rectangle(70, y, 70, 26) };
        setBtn.Click += (_, _) => PickRegion();
        Controls.Add(setBtn);

        var autoBtn = new Button { Text = "Auto-find", Bounds = new Rectangle(146, y, 84, 26) };
        autoBtn.Click += (_, _) => AutoFind();
        Controls.Add(autoBtn);

        var prevBtn = new Button { Text = "Check", Bounds = new Rectangle(236, y, 70, 26) };
        prevBtn.Click += (_, _) => Preview();
        Controls.Add(prevBtn);
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
        _cooldown.Minimum = 100;
        _cooldown.Maximum = 60000;
        _cooldown.Increment = 100;
        _cooldown.Value = Math.Clamp(cfg.CooldownMs, 100, 60000);
        _cooldown.ValueChanged += (_, _) =>
            { _cfg.CooldownMs = (int)_cooldown.Value; _onChange(); };
        Controls.Add(_cooldown);
        Controls.Add(Lab("ms", 170, y + 4));
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

        // The globes live in the bottom corners; searching only there keeps
        // spell effects and the minimap out of the result.
        var vs = SystemInformation.VirtualScreen;
        int w = (int)(vs.Width * 0.22);
        int h = (int)(vs.Height * 0.32);
        var search = _blue
            ? new Rectangle(vs.Right - w, vs.Bottom - h, w, h)
            : new Rectangle(vs.Left, vs.Bottom - h, w, h);

        var found = OrbDetector.AutoLocate(search, _blue);
        owner?.Show();

        if (found is null)
        {
            MessageBox.Show(
                "Couldn't find it. Use Set… and drag the box by hand — that always works.",
                "Auto-find", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Apply(found.Value);
        Preview();
    }

    private void Apply(Rectangle r)
    {
        _cfg.Region = Box.From(r);
        _region.Text = _cfg.Region.ToString();
        _onChange();
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
        owner?.Hide();
        Thread.Sleep(180);
        using var shot = ScreenCapture.Snapshot(_cfg.Region.ToRect());
        owner?.Show();
        using var dlg = new PreviewForm(shot, _cfg, Text);
        dlg.ShowDialog(owner);
    }

    /// <summary>Called from the UI thread with the latest reading.</summary>
    public void Update(GlobeReading r)
    {
        if (!r.Ok || !_cfg.Region.IsValid)
        {
            _pct.Text = "no region";
            _bar.Value = 0;
            return;
        }
        _bar.Value = (int)Math.Clamp(r.Fraction * 1000, 0, 1000);
        _pct.Text = $"{r.Fraction * 100:0.0} %";
    }
}
