namespace P02;

/// <summary>
/// A live view of what the detector sees. It updates continuously and sits on
/// top of the game, so you can drain the globe and watch the reading follow it
/// down. A single still frame of a full globe hid the failure that mattered:
/// reading 100% while the globe was nearly empty.
/// </summary>
public sealed class PreviewForm : Form
{
    private readonly Rectangle _region;
    private readonly WatcherConfig _cfg;
    private readonly Action _onChange;
    private readonly ScreenCapture _cap = new();
    private readonly System.Windows.Forms.Timer _tick = new();

    private readonly PictureBox _pic = new();
    private readonly Label _read = new();
    private readonly Label _calib = new();
    private readonly Label _warn = new();
    private readonly TrackBar _margin = new();
    private readonly TrackBar _minv = new();
    private readonly CheckBox _showMask = new();

    private double _seenLow = 1.0;
    private double _seenHigh;

    public PreviewForm(Rectangle region, WatcherConfig cfg, string title, Action onChange)
    {
        _region = region;
        _cfg = cfg;
        _onChange = onChange;

        Text = $"{title} – what the detector sees (live)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        int pw = Math.Clamp(region.Width, 200, 300);
        int ph = (int)(pw * (region.Height / (double)region.Width));

        _pic.SetBounds(12, 12, pw, ph);
        _pic.SizeMode = PictureBoxSizeMode.StretchImage;
        _pic.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_pic);

        int cx = pw + 24;
        int cw = 320;

        _read.SetBounds(cx, 12, cw, 22);
        _read.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        Controls.Add(_read);

        _calib.SetBounds(cx, 36, cw, 20);
        _calib.ForeColor = SystemColors.GrayText;
        Controls.Add(_calib);

        _warn.SetBounds(cx, 58, cw, 48);
        _warn.ForeColor = Color.FromArgb(180, 60, 0);
        Controls.Add(_warn);

        int y = 112;
        Controls.Add(new Label { Text = "Colour margin", Bounds = new Rectangle(cx, y, 110, 18) });
        _margin.SetBounds(cx + 110, y - 6, 200, 40);
        _margin.Minimum = 4;
        _margin.Maximum = 90;
        _margin.TickFrequency = 10;
        _margin.Value = Math.Clamp(cfg.ColourMargin, 4, 90);
        _margin.ValueChanged += (_, _) => { _cfg.ColourMargin = _margin.Value; _onChange(); };
        Controls.Add(_margin);
        y += 42;

        Controls.Add(new Label { Text = "Min brightness", Bounds = new Rectangle(cx, y, 110, 18) });
        _minv.SetBounds(cx + 110, y - 6, 200, 40);
        _minv.Minimum = 0;
        _minv.Maximum = 200;
        _minv.TickFrequency = 20;
        _minv.Value = Math.Clamp(cfg.MinValue, 0, 200);
        _minv.ValueChanged += (_, _) => { _cfg.MinValue = _minv.Value; _onChange(); };
        Controls.Add(_minv);
        y += 44;

        _showMask.Text = "Show exactly which pixels count as liquid";
        _showMask.SetBounds(cx, y, cw, 22);
        Controls.Add(_showMask);
        y += 28;

        Controls.Add(new Label
        {
            Bounds = new Rectangle(cx, y, cw, 48),
            ForeColor = SystemColors.GrayText,
            Text = "This updates live. Spend the globe and watch the number "
                 + "follow it down — if it stays near 100% while the globe "
                 + "drains, that is the bug, and Tune colours is the fix.",
        });
        y += 54;

        var save = new Button { Text = "Save image", Bounds = new Rectangle(cx, y, 100, 28) };
        save.Click += (_, _) => SaveDebugImage();
        Controls.Add(save);

        var ok = new Button
        {
            Text = "Done",
            Bounds = new Rectangle(cx + cw - 80, y, 80, 28),
            DialogResult = DialogResult.OK,
        };
        Controls.Add(ok);
        AcceptButton = ok;

        ClientSize = new Size(cx + cw + 12, Math.Max(ph + 24, y + 40));

        _tick.Interval = 120;
        _tick.Tick += (_, _) => Refresh_();
        _tick.Start();
        Refresh_();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Left visible to capture on purpose: this is the window people are
        // most often asked to screenshot.
        Native.ExcludeFromCapture(Handle, false);
    }

    private void Refresh_()
    {
        if (!_cap.Grab(_region)) return;

        int h = _cap.Height, w = _cap.Width;
        int surface = OrbDetector.SurfaceRow(_cap.Buffer, w, h, _cfg);
        double frac = OrbDetector.FractionFromRow(surface, h, _cfg);
        int trig = OrbDetector.RowForFraction(_cfg.Threshold, h, _cfg);

        _seenLow = Math.Min(_seenLow, frac);
        _seenHigh = Math.Max(_seenHigh, frac);

        using var shot = ToBitmap(_cap.Buffer, w, h);
        var canvas = _showMask.Checked
            ? OrbDetector.MaskOverlay(shot, _cfg)
            : (Bitmap)shot.Clone();

        using (var g = Graphics.FromImage(canvas))
        {
            int band = Math.Max(1, (int)(w * _cfg.BandFraction));
            int x0 = (w - band) / 2;
            using var bandPen = new Pen(Color.FromArgb(140, 80, 160, 255), 1);
            g.DrawRectangle(bandPen, x0, 0, band - 1, h - 1);

            if (_cfg.FullRow >= 0 && _cfg.EmptyRow > _cfg.FullRow)
            {
                using var calPen = new Pen(Color.FromArgb(190, 255, 120, 255), 1)
                    { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawLine(calPen, 0, _cfg.FullRow, w, _cfg.FullRow);
                g.DrawLine(calPen, 0, _cfg.EmptyRow, w, _cfg.EmptyRow);
            }

            using var trigPen = new Pen(Color.Gold, 1);
            g.DrawLine(trigPen, 0, trig, w, trig);

            using var fillPen = new Pen(Color.Lime, 2);
            g.DrawLine(fillPen, 0, surface, w, surface);
        }

        _pic.Image?.Dispose();
        _pic.Image = canvas;

        _read.Text = $"{frac * 100:0.0} %   (surface row {surface} of {h})";
        _read.ForeColor = frac < _cfg.Threshold
            ? Color.FromArgb(180, 0, 0)
            : SystemColors.ControlText;

        _calib.Text = _cfg.FullRow >= 0 && _cfg.EmptyRow > _cfg.FullRow
            ? $"calibrated rows {_cfg.FullRow}–{_cfg.EmptyRow}   "
              + $"seen since opening: {_seenLow * 100:0}%–{_seenHigh * 100:0}%"
            : "not calibrated — box edges used as full and empty";

        _warn.Text = _seenHigh - _seenLow < 0.02 && _seenHigh > 0.97
            ? "Reading has not moved off 100%. If the globe HAS drained while "
            + "this was open, the empty globe is being counted as liquid."
            : "";
    }

    private static Bitmap ToBitmap(byte[] buf, int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
                                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = w * ScreenCapture.Bpp;
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(
                    buf, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
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
            using var shot = ToBitmap(_cap.Buffer, _cap.Width, _cap.Height);
            using (var mask = OrbDetector.MaskOverlay(shot, _cfg))
            using (var sheet = new Bitmap(shot.Width * 2 + 12, shot.Height))
            using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Color.Black);
                g.DrawImage(shot, 0, 0);
                g.DrawImage(mask, shot.Width + 12, 0);
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
        if (disposing)
        {
            _tick.Stop();
            _tick.Dispose();
            _pic.Image?.Dispose();
            _cap.Dispose();
        }
        base.Dispose(disposing);
    }
}
