using System.ComponentModel;

namespace P02;

/// <summary>
/// A plain fill bar.
///
/// WinForms ProgressBar animates towards a new value when visual styles are on,
/// so the bar slides smoothly behind reality and a globe that drops in one
/// frame appears to drain over half a second. That made monitoring look slow
/// and spotty when the readings underneath were correct and immediate.
/// </summary>
internal sealed class LevelBar : Control
{
    private double _value;
    private bool _below;

    public LevelBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
    }

    /// <summary>Fill fraction, 0-1.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Clamp(value, 0, 1);
            // A pixel is the smallest change worth a repaint.
            if (Math.Abs(v - _value) * Math.Max(1, Width) < 1) return;
            _value = v;
            Invalidate();
        }
    }

    /// <summary>Paints the fill in red once the trigger has been crossed.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Below
    {
        get => _below;
        set { if (_below != value) { _below = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = ClientRectangle;

        // On a transparent overlay the empty part of the bar must stay empty:
        // filling it would paint a solid block over the game.
        bool seeThrough = BackColor == Color.Transparent;
        if (!seeThrough)
            using (var back = new SolidBrush(SystemColors.ControlLight))
                g.FillRectangle(back, r);

        int w = (int)Math.Round((r.Width - 2) * _value);
        if (w > 0)
        {
            var fill = _below
                ? Color.FromArgb(200, 60, 60)
                : Color.FromArgb(60, 160, 70);
            using var brush = new SolidBrush(fill);
            g.FillRectangle(brush, r.X + 1, r.Y + 1, w, r.Height - 2);
        }

        using var pen = new Pen(seeThrough
            ? Color.FromArgb(150, 150, 155)
            : SystemColors.ControlDark);
        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }
}
