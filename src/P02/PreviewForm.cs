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
    private readonly Action _onChange;
    private readonly PictureBox _pic = new();
    private readonly Label _read = new();
    private readonly Label _calib = new();
    private readonly TrackBar _margin = new();
    private readonly TrackBar _minv = new();
    private readonly CheckBox _showMask = new();

    public PreviewForm(Bitmap shot, WatcherConfig cfg, string title, Action onChange)
    {
        _shot = (Bitmap)shot.Clone();
        _buf = ScreenCapture.ToBuffer(_shot);
        _cfg = cfg;
        _onChange = onChange;

        Text = $"{title} – what the detector sees";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        int pw = Math.Clamp(_shot.Width * 2, 200, 420);
        int ph = (int)(pw * (_shot.Height / (double)_shot.Width));

        _pic.SetBounds(12, 12, pw, ph);
        _pic.SizeMode = PictureBoxSizeMode.StretchImage;
        Controls.Add(_pic);

        int y = ph + 20;
        _read.SetBounds(12, y, 460, 20);
        _read.Font = new Font(Font, FontStyle.Bold);
        Controls.Add(_read);
        y += 22;

        _calib.SetBounds(12, y, 460, 20);
        _calib.ForeColor = SystemColors.GrayText;
        Controls.Add(_calib);
        y += 26;

        Controls.Add(new Label { Text = "Colour margin", Bounds = new Rectangle(12, y, 120, 18) });
        _margin.SetBounds(136, y - 6, 240, 40);
        _margin.Minimum = 4;
        _margin.Maximum = 90;
        _margin.TickFrequency = 10;
        _margin.Value = Math.Clamp(cfg.ColourMargin, 4, 90);
        _margin.ValueChanged += (_, _) =>
        {
            _cfg.ColourMargin = _margin.Value;
            _onChange();
            Redraw();
        };
        Controls.Add(_margin);
        y += 42;

        Controls.Add(new Label { Text = "Min brightness", Bounds = new Rectangle(12, y, 120, 18) });
        _minv.SetBounds(136, y - 6, 240, 40);
        _minv.Minimum = 0;
        _minv.Maximum = 200;
        _minv.TickFrequency = 20;
        _minv.Value = Math.Clamp(cfg.MinValue, 0, 200);
        _minv.ValueChanged += (_, _) =>
        {
            _cfg.MinValue = _minv.Value;
            _onChange();
            Redraw();
        };
        Controls.Add(_minv);
        y += 44;

        _showMask.Text = "Show exactly which pixels count as liquid";
        _showMask.SetBounds(12, y, 320, 22);
        _showMask.CheckedChanged += (_, _) => Redraw();
        Controls.Add(_showMask);
        y += 26;

        Controls.Add(new Label
        {
            Bounds = new Rectangle(12, y, 420, 34),
            ForeColor = SystemColors.GrayText,
            Text = "Green = liquid surface it found. Yellow = your trigger point.\n" +
                   "If a full globe does not read 100%, use Full = 100%, not the sliders.",
        });
        y += 40;

        var save = new Button { Text = "Save image", Bounds = new Rectangle(12, y, 100, 28) };
        save.Click += (_, _) => SaveDebugImage();
        Controls.Add(save);

        var ok = new Button
        {
            Text = "Done",
            Bounds = new Rectangle(Math.Max(pw - 68, 340), y, 80, 28),
            DialogResult = DialogResult.OK,
        };
        Controls.Add(ok);
        AcceptButton = ok;

        ClientSize = new Size(Math.Max(pw + 24, 440), y + 40);
        Redraw();
    }

    private void Redraw()
    {
        int surface = OrbDetector.SurfaceRow(_buf, _shot.Width, _shot.Height, _cfg);
        double frac = OrbDetector.FractionFromRow(surface, _shot.Height, _cfg);
        int trig = OrbDetector.RowForFraction(_cfg.Threshold, _shot.Height, _cfg);

        var canvas = _showMask.Checked
            ? OrbDetector.MaskOverlay(_shot, _cfg)
            : (Bitmap)_shot.Clone();

        using (var g = Graphics.FromImage(canvas))
        {
            int band = Math.Max(1, (int)(canvas.Width * _cfg.BandFraction));
            int x0 = (canvas.Width - band) / 2;
            using var bandPen = new Pen(Color.FromArgb(140, 80, 160, 255), 1);
            g.DrawRectangle(bandPen, x0, 0, band - 1, canvas.Height - 1);

            if (_cfg.FullRow >= 0 && _cfg.EmptyRow > _cfg.FullRow)
            {
                using var calPen = new Pen(Color.FromArgb(190, 255, 120, 255), 1)
                    { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawLine(calPen, 0, _cfg.FullRow, canvas.Width, _cfg.FullRow);
                g.DrawLine(calPen, 0, _cfg.EmptyRow, canvas.Width, _cfg.EmptyRow);
            }

            using var trigPen = new Pen(Color.Gold, 1);
            g.DrawLine(trigPen, 0, trig, canvas.Width, trig);

            using var fillPen = new Pen(Color.Lime, 2);
            g.DrawLine(fillPen, 0, surface, canvas.Width, surface);
        }

        _pic.Image?.Dispose();
        _pic.Image = canvas;

        _read.Text = $"reads {frac * 100:0.0} %   (surface at row {surface} of {_shot.Height})";
        _calib.Text = _cfg.FullRow >= 0 && _cfg.EmptyRow > _cfg.FullRow
            ? $"calibrated: full = row {_cfg.FullRow}, empty = row {_cfg.EmptyRow} (pink)"
            : "not calibrated — the box edges are being used as full and empty";
    }

    /// <summary>Writes the current view to disk, for when a read needs
    /// explaining to someone who cannot see the screen.</summary>
    private void SaveDebugImage()
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            string path = Path.Combine(
                AppConfig.Dir, $"{Text.Split(' ')[0]}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            using (var mask = OrbDetector.MaskOverlay(_shot, _cfg))
            using (var sheet = new Bitmap(_shot.Width * 2 + 12, _shot.Height))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.Black);
                g.DrawImage(_shot, 0, 0);
                g.DrawImage(mask, _shot.Width + 12, 0);
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save image",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _pic.Image?.Dispose(); _shot.Dispose(); }
        base.Dispose(disposing);
    }
}
