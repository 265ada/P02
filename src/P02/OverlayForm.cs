using System.Drawing.Drawing2D;

namespace P02;

/// <summary>
/// The small readout that sits over the game.
///
/// Drawn by hand rather than assembled from labels. Over a moving, brightly lit
/// game, text needs its own contrast to stay legible, bars need to read at a
/// glance rather than be studied, and the whole thing has to stay out of the
/// way - none of which a grid of grey system controls manages.
/// </summary>
public sealed class OverlayForm : Form
{
    private const int Pad = 10;
    private const int RowH = 22;
    private const int BarH = 12;

    private readonly System.Windows.Forms.Timer _fade = new();

    private GlobeReading _life = new("Life", 0, false);
    private GlobeReading _mana = new("Mana", 0, false, "off");
    private double _lifeTrigger = 0.5;
    private double _manaTrigger = 0.3;
    private bool _armed;
    private bool _firing;
    private int _fired;
    private bool _inCombat;
    private string _detail = "";

    private Point _grabbedAt;
    private Point _wasAt;
    private bool _dragging;

    /// <summary>Raised when a drag finishes, so the position can be saved.</summary>
    public event Action? Moved;

    public OverlayForm()
    {
        Text = "P02";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        ClientSize = new Size(268, 96);

        // Everything painted in this colour is punched through, so the window
        // is only ever its own contents - no panel sitting over the game.
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        _fade.Interval = 220;
        _fade.Tick += (_, _) => { _fade.Stop(); _firing = false; Invalidate(); };

        MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _grabbedAt = Cursor.Position;
            _wasAt = Location;
            Cursor = Cursors.SizeAll;
        };
        MouseMove += (_, _) =>
        {
            if (!_dragging) return;
            var now = Cursor.Position;
            Location = new Point(_wasAt.X + now.X - _grabbedAt.X,
                                 _wasAt.Y + now.Y - _grabbedAt.Y);
        };
        MouseUp += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            Cursor = Cursors.Default;
            Moved?.Invoke();
        };
    }

    public void Show(GlobeReading life, GlobeReading mana,
                     double lifeTrigger, double manaTrigger)
    {
        _life = life;
        _mana = mana;
        _lifeTrigger = lifeTrigger;
        _manaTrigger = manaTrigger;

        _detail = life.Note.Length > 0
            ? life.Note
            : life.TextRaw.Length > 0
                ? life.TextRaw.Replace("memory, life ", "").Replace("numbers, ", "")
                : "globe pixels";

        FitHeight();
        Invalidate();
    }

    public void SetArmed(bool armed)
    {
        _armed = armed;
        Invalidate();
    }

    public void SetFightCount(int fired, bool inCombat)
    {
        if (fired == _fired && inCombat == _inCombat) return;
        _fired = fired;
        _inCombat = inCombat;
        Invalidate();
    }

    /// <summary>Blinks when a key is actually sent.</summary>
    public void Fired()
    {
        _firing = true;
        Invalidate();
        _fade.Stop();
        _fade.Start();
    }

    /// <summary>A globe that is not watched has nothing to say, so it takes no room.</summary>
    private bool ManaShown => !(_mana.Note == "off" && !_mana.Ok);

    private void FitHeight()
    {
        int h = Pad + RowH + (ManaShown ? RowH : 0) + 30 + Pad;
        if (ClientSize.Height != h) ClientSize = new Size(ClientSize.Width, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int y = Pad;
        Row(g, "Life", _life, _lifeTrigger, y);
        y += RowH;

        if (ManaShown)
        {
            Row(g, "Mana", _mana, _manaTrigger, y);
            y += RowH;
        }

        Status(g, y + 6);
    }

    private void Row(Graphics g, string name, GlobeReading r, double trigger, int y)
    {
        Glyph(g, name, Theme.Dim, Theme.Small, Pad, y + 1);

        int barX = Pad + 34;
        var bar = new Rectangle(barX, y + 3, ClientSize.Width - barX - 54, BarH);

        // A dark bed under the bar: an unbacked bar is unreadable over bright
        // ground, which is most of a game.
        using (var bed = new SolidBrush(Color.FromArgb(190, 16, 17, 20)))
        using (var path = Rounded(bar, BarH / 2))
            g.FillPath(bed, path);

        bool held = r.Note.Length > 0;
        if (r.Ok && !held)
        {
            int w = (int)Math.Round((bar.Width - 2) * Math.Clamp(r.Fraction, 0, 1));
            if (w > 3)
            {
                var fill = r.Fraction < trigger ? Theme.Bad : Theme.Good;
                var inner = new Rectangle(bar.X + 1, bar.Y + 1, w, bar.Height - 2);
                using var brush = new LinearGradientBrush(inner,
                    ControlPaint.Light(fill, 0.2f), fill, LinearGradientMode.Vertical);
                using var path = Rounded(inner, (bar.Height - 2) / 2);
                g.FillPath(brush, path);
            }

            // Where the trigger sits, so a glance says how much room is left
            // rather than only where the level is.
            int tx = bar.X + 1 + (int)((bar.Width - 2) * Math.Clamp(trigger, 0, 1));
            using var tick = new Pen(Color.FromArgb(170, 255, 255, 255));
            g.DrawLine(tick, tx, bar.Y + 1, tx, bar.Bottom - 2);
        }

        using (var edge = new Pen(Color.FromArgb(110, 255, 255, 255)))
        using (var path = Rounded(bar, BarH / 2))
            g.DrawPath(edge, path);

        string right = !r.Ok ? (r.Note.Length > 0 ? r.Note : "--")
                     : held ? "held"
                     : $"{r.Fraction * 100:0}%";
        var colour = !r.Ok ? Theme.Dim
                   : held ? Theme.Warn
                   : r.Fraction < trigger ? Theme.Bad : Theme.Text;
        Glyph(g, right, colour, Theme.UiBold, bar.Right + 7, y + 1);
    }

    private void Status(Graphics g, int y)
    {
        Glyph(g, _armed ? "ARMED" : "disarmed",
              _armed ? Theme.Armed : Theme.Dim, Theme.UiBold, Pad, y);

        string detail = _detail.Length > 24 ? _detail[..24] : _detail;
        var detailColour = _life.Note.Length > 0 || !_life.FromText ? Theme.Warn : Theme.Dim;
        Glyph(g, detail, detailColour, Theme.Small, Pad + 60, y + 1);

        if (_firing)
            Glyph(g, "FIRE", Theme.Good, Theme.UiBold, ClientSize.Width - 76, y);

        if (_inCombat || _fired > 0)
            Glyph(g, _fired.ToString(), _inCombat ? Theme.Warn : Theme.Dim,
                  Theme.UiBold, ClientSize.Width - 28, y);
    }

    /// <summary>
    /// Text with a dark halo behind it. The ground behind a word changes
    /// constantly over a game and no single colour stays readable against all
    /// of it; an outline does.
    /// </summary>
    private static void Glyph(Graphics g, string s, Color colour, Font font, int x, int y)
    {
        if (s.Length == 0) return;
        using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                g.DrawString(s, font, shadow, x + dx, y + dy);

        using var brush = new SolidBrush(colour);
        g.DrawString(s, font, brush, x, y);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 90, 180);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ExcludeFromCapture(Handle, false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fade.Dispose();
        base.Dispose(disposing);
    }
}
