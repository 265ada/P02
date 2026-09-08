using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace P02;

/// <summary>
/// A titled panel with a soft border and an accent stripe.
///
/// Replaces GroupBox, which draws its own frame and caption in system colours
/// and cannot be talked out of it - which on a dark window looks exactly like a
/// bug. Same coordinates, so nothing inside has to move.
/// </summary>
public class Card : Panel
{
    private Color _accent = Theme.Accent;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
    }

    /// <summary>The stripe down the left of the title, for telling cards apart.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent
    {
        get => _accent;
        set { _accent = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var back = new SolidBrush(Theme.Card))
        using (var path = Rounded(r, 8))
            g.FillPath(back, path);

        using (var pen = new Pen(Theme.Line))
        using (var path = Rounded(r, 8))
            g.DrawPath(pen, path);

        if (Text.Length > 0)
        {
            using var stripe = new SolidBrush(_accent);
            g.FillRectangle(stripe, 12, 12, 3, 15);

            using var title = new SolidBrush(Theme.Text);
            g.DrawString(Text, Theme.Title, title, 21, 9);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
