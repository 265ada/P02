namespace P02;

/// <summary>
/// One palette and one set of fonts, applied over the whole window.
///
/// Everything here is coordinate-laid-out already, so this deliberately styles
/// existing controls rather than rebuilding them: colours, fonts and flat
/// borders, walked over the control tree. Restyling is then a change in one
/// place instead of two hundred.
/// </summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(24, 25, 28);
    public static readonly Color Card = Color.FromArgb(33, 35, 39);
    public static readonly Color Field = Color.FromArgb(43, 46, 51);
    public static readonly Color Line = Color.FromArgb(58, 62, 69);
    public static readonly Color Text = Color.FromArgb(232, 234, 237);
    public static readonly Color Dim = Color.FromArgb(150, 156, 165);
    public static readonly Color Accent = Color.FromArgb(88, 156, 246);
    public static readonly Color Good = Color.FromArgb(96, 200, 120);
    public static readonly Color Warn = Color.FromArgb(240, 180, 70);
    public static readonly Color Bad = Color.FromArgb(238, 96, 90);
    public static readonly Color Armed = Color.FromArgb(206, 66, 66);

    public static readonly Font Ui = new("Segoe UI", 9f);
    public static readonly Font UiBold = new("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Title = new("Segoe UI Semibold", 10.5f);
    public static readonly Font Big = new("Segoe UI Semibold", 12f);

    /// <summary>
    /// Draws a checkbox over the top of the system one.
    ///
    /// Ticked is a filled accent box with a white check; unticked is an empty
    /// outline. The difference has to survive being glanced at over a game.
    /// </summary>
    private static void DrawTick(CheckBox cb, Graphics g)
    {
        const int Size = 15;
        var box = new Rectangle(0, (cb.Height - Size) / 2, Size, Size);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var clear = new SolidBrush(cb.BackColor))
            g.FillRectangle(clear, new Rectangle(box.X, 0, box.Width + 2, cb.Height));

        using (var back = new SolidBrush(cb.Checked ? Accent : Field))
        using (var path = RoundRect(box, 3))
            g.FillPath(back, path);

        using (var edge = new Pen(cb.Checked ? Accent : Line, 1.4f))
        using (var path = RoundRect(box, 3))
            g.DrawPath(edge, path);

        if (!cb.Checked) return;

        using var tick = new Pen(Color.White, 2f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        g.DrawLines(tick,
        [
            new PointF(box.X + 3.5f, box.Y + 7.5f),
            new PointF(box.X + 6f, box.Y + 10.5f),
            new PointF(box.X + 11.5f, box.Y + 4.5f),
        ]);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Styles a control and everything inside it.</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Line;
                    b.FlatAppearance.MouseOverBackColor = Field;
                    b.FlatAppearance.MouseDownBackColor = Line;
                    b.BackColor = Card;
                    b.ForeColor = Text;
                    b.Font = Ui;
                    b.Cursor = Cursors.Hand;
                    break;

                case CheckBox cb:
                    cb.FlatStyle = FlatStyle.Flat;
                    cb.FlatAppearance.BorderSize = 0;
                    cb.BackColor = root.BackColor;
                    cb.ForeColor = Text;
                    cb.Font = Ui;
                    cb.Cursor = Cursors.Hand;

                    // The system glyph on a dark card is a pale square that
                    // looks the same ticked or not, and tinting its background
                    // only made it a slightly different pale square. Drawn over
                    // instead: an empty outline, or a filled box with a tick in
                    // it, which is legible across a room.
                    cb.Paint += (sender, e) => DrawTick((CheckBox)sender!, e.Graphics);
                    cb.CheckedChanged += (sender, _) => ((CheckBox)sender!).Invalidate();
                    cb.MouseEnter += (sender, _) => ((CheckBox)sender!).Invalidate();
                    cb.MouseLeave += (sender, _) => ((CheckBox)sender!).Invalidate();
                    break;

                case NumericUpDown n:
                    n.BorderStyle = BorderStyle.FixedSingle;
                    n.BackColor = Field;
                    n.ForeColor = Text;
                    n.Font = Ui;
                    break;

                case TextBox t:
                    t.BorderStyle = BorderStyle.FixedSingle;
                    t.BackColor = Field;
                    t.ForeColor = Text;
                    t.Font = Ui;
                    break;

                case ComboBox cbo:
                    cbo.FlatStyle = FlatStyle.Flat;
                    cbo.BackColor = Field;
                    cbo.ForeColor = Text;
                    cbo.Font = Ui;
                    break;

                case LinkLabel ll:
                    ll.LinkColor = Accent;
                    ll.ActiveLinkColor = Text;
                    ll.BackColor = Color.Transparent;
                    ll.Font = Ui;
                    break;

                case Label l:
                    l.BackColor = Color.Transparent;
                    // Anything already coloured is saying something - a warning,
                    // a good reading - so leave it alone and only take over the
                    // ones still on the system default.
                    if (l.ForeColor == SystemColors.ControlText) l.ForeColor = Text;
                    else if (l.ForeColor == SystemColors.GrayText) l.ForeColor = Dim;
                    if (l.Font == Control.DefaultFont) l.Font = Ui;
                    break;

                case Panel p:
                    if (p.BackColor == SystemColors.Control) p.BackColor = Card;
                    break;
            }

            if (c.HasChildren) Apply(c);
        }
    }

    /// <summary>
    /// A button that is meant to be found rather than read past.
    ///
    /// Everything on that row looks the same, and two of them are the ones
    /// anybody actually needs: the one that sets the whole thing up, and the
    /// one that fetches a fix. Colour is how you find a button without reading
    /// five labels first.
    /// </summary>
    public static void Primary(Button b, Color fill)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(fill, 0.25f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(fill, 0.1f);
        b.BackColor = fill;
        b.ForeColor = Color.FromArgb(18, 20, 23);
        b.Font = new Font("Segoe UI Semibold", 9.5f);
        b.Cursor = Cursors.Hand;
    }
}
