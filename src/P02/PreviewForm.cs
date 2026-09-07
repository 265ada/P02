namespace P02;

/// <summary>
/// Shows what the detector sees: where it thinks the liquid surface is, and
/// where your trigger sits. The two sliders tune the colour test live, which is
/// the only fiddly part of setup.
/// </summary>
public sealed class PreviewForm : Form
{
    private readonly Bitmap _shot;
    private readonly byte[] _buf;
    private readonly WatcherConfig _cfg;
    private readonly PictureBox _pic = new();
    private readonly Label _read = new();
    private readonly TrackBar _ratio = new();
    private readonly TrackBar _minv = new();

    public PreviewForm(Bitmap shot, WatcherConfig cfg, string title)
    {
        _shot = (Bitmap)shot.Clone();
        _buf = ScreenCapture.ToBuffer(_shot);
        _cfg = cfg;

        Text = $"{title} – what the detector sees";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        int pw = Math.Clamp(_shot.Width * 2, 160, 420);
        int ph = (int)(pw * (_shot.Height / (double)_shot.Width));

        _pic.SetBounds(12, 12, pw, ph);
        _pic.SizeMode = PictureBoxSizeMode.StretchImage;
        Controls.Add(_pic);

        int y = ph + 20;
        _read.SetBounds(12, y, pw + 200, 20);
        _read.Font = new Font(Font, FontStyle.Bold);
        Controls.Add(_read);
        y += 26;

        Controls.Add(new Label { Text = "Colour strictness", Bounds = new Rectangle(12, y, 120, 18) });
        _ratio.SetBounds(136, y - 6, 240, 40);
        _ratio.Minimum = 100; _ratio.Maximum = 320; _ratio.TickFrequency = 20;
        _ratio.Value = Math.Clamp((int)(cfg.ChannelRatio * 100), 100, 320);
        _ratio.ValueChanged += (_, _) => { _cfg.ChannelRatio = _ratio.Value / 100.0; Redraw(); };
        Controls.Add(_ratio);
        y += 44;

        Controls.Add(new Label { Text = "Min brightness", Bounds = new Rectangle(12, y, 120, 18) });
        _minv.SetBounds(136, y - 6, 240, 40);
        _minv.Minimum = 0; _minv.Maximum = 200; _minv.TickFrequency = 20;
        _minv.Value = Math.Clamp(cfg.MinValue, 0, 200);
        _minv.ValueChanged += (_, _) => { _cfg.MinValue = _minv.Value; Redraw(); };
        Controls.Add(_minv);
        y += 46;

        Controls.Add(new Label
        {
            Bounds = new Rectangle(12, y, 400, 34),
            Text = "Green = liquid surface it found. Yellow = your trigger point.\n" +
                   "Green should sit exactly on the surface at any fill level.",
            ForeColor = SystemColors.GrayText,
        });
        y += 42;

        var ok = new Button { Text = "Done", Bounds = new Rectangle(pw - 68, y, 80, 28), DialogResult = DialogResult.OK };
        Controls.Add(ok);
        AcceptButton = ok;

        ClientSize = new Size(Math.Max(pw + 24, 400), y + 40);
        Redraw();
    }

    private void Redraw()
    {
        double frac = OrbDetector.Fraction(_buf, _shot.Width, _shot.Height, _cfg);
        int fill = OrbDetector.FillLine(frac, _shot.Height);
        int trig = OrbDetector.FillLine(_cfg.Threshold, _shot.Height);

        var canvas = (Bitmap)_shot.Clone();
        using (var g = Graphics.FromImage(canvas))
        {
            int band = Math.Max(1, (int)(canvas.Width * _cfg.BandFraction));
            int x0 = (canvas.Width - band) / 2;
            using var bandPen = new Pen(Color.FromArgb(120, 80, 160, 255), 1);
            g.DrawRectangle(bandPen, x0, 0, band, canvas.Height - 1);

            using var trigPen = new Pen(Color.Gold, 1);
            g.DrawLine(trigPen, 0, trig, canvas.Width, trig);

            using var fillPen = new Pen(Color.Lime, 2);
            g.DrawLine(fillPen, 0, fill, canvas.Width, fill);
        }

        _pic.Image?.Dispose();
        _pic.Image = canvas;
        _read.Text = $"reads {frac * 100:0.0} %   (surface at row {fill} of {_shot.Height})";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pic.Image?.Dispose(); _shot.Dispose(); }
        base.Dispose(disposing);
    }
}
