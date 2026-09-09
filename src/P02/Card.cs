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

        // No outline. A surface a shade lighter than the window is separation
        // enough, and a window full of outlined boxes reads as a form to fill
        // in rather than something to glance at mid-fight.
        using (var back = new SolidBrush(Theme.Card))
        using (var path = Rounded(r, 6))
            g.FillPath(back, path);

        if (Text.Length == 0) return;

        // The rule under the heading is the accent, and it is the only
        // structural line on the card: it says where this group begins, which
        // is information. It runs the width of its own title, not the card, so
        // it reads as belonging to the words rather than boxing them.
        using var title = new SolidBrush(Theme.Text);
        var size = g.MeasureString(Text, Theme.Title);
        g.DrawString(Text, Theme.Title, title, 13, 8);

        using var rule = new SolidBrush(_accent);
        g.FillRectangle(rule, 12, 10 + (int)size.Height, Math.Max(24, (int)size.Width - 4), 2);
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
