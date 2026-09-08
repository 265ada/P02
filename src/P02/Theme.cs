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
                    cb.FlatAppearance.BorderColor = Line;
                    // A flat checkbox fills its box with its own BackColor, so
                    // a transparent one came out white on a dark card - and a
                    // white square reads the same whether it is ticked or not.
                    // Matching the card makes it a box again; the accent fill
                    // is what says checked, at a glance and from across a room.
                    cb.BackColor = root.BackColor;
                    cb.FlatAppearance.CheckedBackColor = Accent;
                    cb.FlatAppearance.MouseOverBackColor = Field;
                    cb.ForeColor = Text;
                    cb.Font = Ui;
                    cb.Cursor = Cursors.Hand;
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
}
