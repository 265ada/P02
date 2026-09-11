using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace P02;

/// <summary>
/// The orb, turning, in the corner of the window.
///
/// Every frame is drawn once when the size settles and then cycled, rather
/// than being generated on each tick: a frame is a few hundred gradient fills,
/// and this sits in an application whose whole job is to notice a number
/// changing sixty times a second. Flair that competes with that is not flair.
///
/// Thirty-six frames at twelve a second is one turn every three seconds -
/// slow enough to read as weather inside glass rather than as a spinner
/// waiting for something.
/// </summary>
public sealed class OrbBadge : Control
{
    private Bitmap[] _frames = [];
    private int _at;
    private Size _drawnFor;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 83 };

    public OrbBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;

        _tick.Tick += (_, _) =>
        {
            if (_frames.Length == 0 || !Visible) return;
            _at = (_at + 1) % _frames.Length;
            Invalidate();
        };
    }

    /// <summary>Turning is decoration; it stops whenever nobody can see it.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) _tick.Start(); else _tick.Stop();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Rebuild();
    }

    private void Rebuild()
    {
        if (Width <= 2 || Height <= 2 || _drawnFor == Size) return;
        _drawnFor = Size;

        foreach (var old in _frames) old.Dispose();

        int side = Math.Min(Width, Height);
        var made = new Bitmap[36];
        for (int i = 0; i < made.Length; i++)
            made[i] = Orb.Render(side, i / (double)made.Length);

        _frames = made;
        _at = 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_frames.Length == 0) Rebuild();
        if (_frames.Length == 0) return;

        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var frame = _frames[_at];
        e.Graphics.DrawImage(frame,
            (Width - frame.Width) / 2, (Height - frame.Height) / 2,
            frame.Width, frame.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Stop();
            _tick.Dispose();
            foreach (var f in _frames) f.Dispose();
            _frames = [];
        }
        base.Dispose(disposing);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override string Text { get => base.Text; set => base.Text = value; }
}
