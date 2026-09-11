using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace P02;

/// <summary>
/// What the app has to say, down the side of the window instead of in front of
/// it.
///
/// Everything here used to arrive as a modal box. A box stops the game, has to
/// be dismissed before anything else can happen, and is gone the moment it is -
/// so the one time it said something that mattered, it said it while a fight
/// was going on and then erased itself. Worse, a box that appears often is a
/// box people learn to click through without reading, which is the whole of
/// what went wrong with the setup warning.
///
/// A panel says the same things without interrupting, keeps them where they can
/// be read afterwards, and lets severity be a colour rather than an icon nobody
/// looks at.
/// </summary>
public sealed class NoticeBoard : Panel
{
    public enum Level { Info, Warn, Alert }

    private sealed record Notice(DateTime At, Level Level, string What, string Detail);

    private readonly List<Notice> _notices = [];
    private const int Keep = 200;

    public NoticeBoard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Font = Theme.Ui;
        AutoScroll = true;
        DoubleBuffered = true;
    }

    /// <summary>Raised when something worth noticing arrives, for the tray ping.</summary>
    public event Action<Level, string>? Arrived;

    /// <summary>
    /// Follows the log as it is written, so the panel is a running account of
    /// what the app is doing rather than a list of complaints.
    ///
    /// Not all of it. The poll line lands every two seconds and the fire trail
    /// is hundreds of lines per press - both belong in the file, where they can
    /// be read at leisure, and neither is something to watch scroll past. What
    /// is left is the things that happen because something changed.
    /// </summary>
    public void FollowLog()
    {
        Log.Line += line =>
        {
            if (Worth(line) is not { } level) return;
            Say(level, Tidy(line));
        };
    }

    private static Level? Worth(string line)
    {
        // The trail is indented; the poll line is a heartbeat.
        if (line.Length == 0 || line[0] == ' ') return null;
        if (line.StartsWith("watch ", StringComparison.Ordinal)) return null;

        foreach (string loud in Loud)
            if (line.Contains(loud, StringComparison.OrdinalIgnoreCase))
                return Level.Alert;

        foreach (string odd in Notable)
            if (line.Contains(odd, StringComparison.OrdinalIgnoreCase))
                return Level.Warn;

        return Level.Info;
    }

    /// <summary>Things that have gone wrong, or that fired.</summary>
    private static readonly string[] Loud =
    [
        "FIRED", "died", "failed", "cannot", "could not", "went bad",
    ];

    /// <summary>Things worth a second look, but not a failure.</summary>
    private static readonly string[] Notable =
    [
        "garbled", "searching again", "out of date", "does not contain",
        "stale", "dropped", "not guessing", "re-read", "looking for the lines",
        "different character", "wrong structure", "disagree",
    ];

    /// <summary>Drops the leading label the log uses to group its own lines.</summary>
    private static string Tidy(string line)
    {
        string[] known = ["memory: ", "numbers: ", "setup: ", "overlay: ", "=== "];
        foreach (string head in known)
            if (line.StartsWith(head, StringComparison.Ordinal))
                return line[head.Length..];
        return line;
    }

    public void Say(Level level, string what, string detail = "")
    {
        if (InvokeRequired) { BeginInvoke(() => Say(level, what, detail)); return; }

        // The same thing twice running is one thing. A repair that runs every
        // minute should read as a repair that runs every minute, not as sixty
        // separate events pushing everything else off the panel.
        var last = _notices.Count > 0 ? _notices[0] : null;
        if (last is not null && last.What == what && last.Detail == detail)
        {
            _notices[0] = last with { At = DateTime.Now };
            Rebuild();
            return;
        }

        _notices.Insert(0, new Notice(DateTime.Now, level, what, detail));
        if (_notices.Count > Keep) _notices.RemoveRange(Keep, _notices.Count - Keep);

        Rebuild();
        Arrived?.Invoke(level, what);
    }

    public void Clear()
    {
        _notices.Clear();
        Rebuild();
    }

    /// <summary>Everything on the board as plain text, for pasting somewhere.</summary>
    public string AsText()
    {
        var text = new System.Text.StringBuilder();
        foreach (var n in _notices)
        {
            text.AppendLine($"{n.At:HH:mm:ss}  {n.Level.ToString().ToUpperInvariant()}  {n.What}");
            if (n.Detail.Length > 0) text.AppendLine($"          {n.Detail}");
        }
        return text.ToString();
    }

    private void Rebuild()
    {
        Invalidate();
        AutoScrollPosition = new Point(0, 0);
    }

    private static Color Ink(Level level) => level switch
    {
        Level.Alert => Theme.Bad,
        Level.Warn => Theme.Warn,
        _ => Theme.Dim,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(Theme.Card))
        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 6))
            g.FillPath(back, path);

        int pad = 12;
        int wide = Width - pad * 2 - SystemInformation.VerticalScrollBarWidth;
        int y = pad + AutoScrollPosition.Y;

        using var head = new SolidBrush(Theme.Text);
        g.DrawString("What it is doing", Theme.Title, head, pad, y);
        y += 24;

        if (_notices.Count == 0)
        {
            using var quiet = new SolidBrush(Theme.Dim);
            g.DrawString("Nothing to report. Anything worth telling you appears here "
                         + "rather than in a box over the game.",
                         Theme.Small, quiet,
                         new RectangleF(pad, y, wide, 60));
            return;
        }

        foreach (var n in _notices)
        {
            var ink = Ink(n.Level);

            // A stripe rather than a background wash: colour that says which
            // line it belongs to without making the words harder to read,
            // which is the failure of every coloured log panel there is.
            var whatSize = g.MeasureString(n.What, Theme.UiBold,
                                           wide - 10);
            int h = (int)whatSize.Height;

            SizeF detailSize = SizeF.Empty;
            if (n.Detail.Length > 0)
            {
                detailSize = g.MeasureString(n.Detail, Theme.Small, wide - 10);
                h += (int)detailSize.Height + 2;
            }

            if (y + h > 0 && y < Height)
            {
                using (var stripe = new SolidBrush(ink))
                    g.FillRectangle(stripe, pad, y + 2, 3, h - 2);

                using (var when = new SolidBrush(Theme.Dim))
                    g.DrawString(n.At.ToString("HH:mm"), Theme.Small, when,
                                 Width - pad - 34, y);

                using (var words = new SolidBrush(
                           n.Level == Level.Info ? Theme.Text : ink))
                    g.DrawString(n.What, Theme.UiBold, words,
                                 new RectangleF(pad + 10, y, wide - 44, whatSize.Height));

                if (n.Detail.Length > 0)
                {
                    using var quiet = new SolidBrush(Theme.Dim);
                    g.DrawString(n.Detail, Theme.Small, quiet,
                                 new RectangleF(pad + 10, y + whatSize.Height + 1,
                                                wide - 10, detailSize.Height));
                }
            }

            y += h + 12;
        }

        // Room to scroll to the oldest entry.
        int total = y - AutoScrollPosition.Y + pad;
        if (AutoScrollMinSize.Height != total)
            AutoScrollMinSize = new Size(0, total);
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

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override bool AutoScroll
    {
        get => base.AutoScroll;
        set => base.AutoScroll = value;
    }
}
