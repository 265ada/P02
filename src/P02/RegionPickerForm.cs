namespace P02;

/// <summary>
/// Dimmed full-desktop overlay: drag a box, get screen coordinates back.
/// Spans every monitor, so it works with the game borderless on any display.
/// </summary>
public sealed class RegionPickerForm : Form
{
    private Point _start;
    private Rectangle _sel;
    private bool _dragging;
    private readonly string _prompt;
    private readonly Rectangle _virtual;

    public Rectangle? Selection { get; private set; }

    public RegionPickerForm(string prompt)
    {
        _prompt = prompt;
        _virtual = SystemInformation.VirtualScreen;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        Opacity = 0.35;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _start = e.Location;
        _sel = new Rectangle(e.X, e.Y, 0, 0);
        _dragging = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        _sel = Rectangle.FromLTRB(
            Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
            Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        if (_sel.Width >= 4 && _sel.Height >= 4)
        {
            // Client coords are relative to the virtual desktop origin, which
            // is negative when a monitor sits left of or above the primary.
            Selection = new Rectangle(_sel.X + _virtual.Left, _sel.Y + _virtual.Top,
                                      _sel.Width, _sel.Height);
            DialogResult = DialogResult.OK;
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var font = new Font("Segoe UI", 14, FontStyle.Bold);
        var text = _prompt + "     (Esc to cancel)";
        var size = g.MeasureString(text, font);
        g.DrawString(text, font, Brushes.Lime,
                     (Width - size.Width) / 2, 30);

        if (_sel.Width > 0 && _sel.Height > 0)
        {
            using var pen = new Pen(Color.Lime, 2);
            g.DrawRectangle(pen, _sel);
            g.DrawString($"{_sel.Width} x {_sel.Height}", font, Brushes.Lime,
                         _sel.Left, Math.Max(0, _sel.Top - 26));
        }
    }

    /// <summary>Runs the overlay; null if cancelled.</summary>
    public static Rectangle? Pick(string prompt)
    {
        using var f = new RegionPickerForm(prompt);
        return f.ShowDialog() == DialogResult.OK ? f.Selection : null;
    }
}
