using System.Diagnostics;

namespace P02;

/// <summary>
/// Finds the floating life bar drawn over your character.
///
/// It is not at a fixed place - it follows the character, and it is not drawn
/// at all much of the time. So it has to be looked for, which is why this is
/// separate from everything else here: every other reading comes from a box
/// somebody set once.
///
/// It hunts the blue mana bar, not the green life bar, for two reasons.
///
/// Totems and minions have life bars and no mana - so hunting green meant
/// competing with everything on screen that has hit points, and losing to
/// whichever happened to be nearest the middle. Nothing but the character has
/// the blue one.
///
/// And the readout is meant to sit over the life bar, as part of the game's own
/// interface. Anchoring to the thing you intend to cover cannot work; the mana
/// bar sits directly under it and stays visible.
/// </summary>
internal sealed class BarFinder : IDisposable
{
    private const int MinRun = 26;      // shorter than this is a health globe pip or a gem
    private const int MaxThick = 14;    // the pair is thin; anything deep is scenery
    private const int Reacquire = 140;  // how far from the last sighting to look first

    private readonly ScreenCapture _cap = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Rectangle _last;
    private long _lastSeenMs = long.MinValue / 2;
    private long _lastLookMs = long.MinValue / 2;

    /// <summary>Where the bar was last seen, in screen coordinates.</summary>
    public Rectangle Bar => _last;

    /// <summary>Whether it is on screen right now.</summary>
    public bool Visible { get; private set; }

    /// <summary>
    /// Looks for the bar inside the game's window. Rate limited on its own,
    /// because this is decoration - it must never compete with the reading that
    /// decides whether to press a key.
    /// </summary>
    /// <summary>
    /// Where not to look: the readout's own window.
    ///
    /// It draws a green bar with a blue one under it, over the character, which
    /// is precisely the thing being searched for - so it was covering the real
    /// bar and offering itself as a replacement.
    /// </summary>
    public Rectangle Ignore { get; set; }

    public void Look(Rectangle window, int everyMs = 80)
    {
        long now = _clock.ElapsedMilliseconds;
        if (now - _lastLookMs < everyMs) return;
        _lastLookMs = now;

        if (window.Width < 100 || window.Height < 100) { Lost(now); return; }

        // Near where it was last, first. The bar moves with a character, which
        // is to say slowly, and a small search is a small capture.
        if (Visible || now - _lastSeenMs < 1500)
        {
            var near = Rectangle.Inflate(_last, Reacquire, Reacquire);
            near.Intersect(window);
            if (near.Width > 40 && near.Height > 20 && Scan(near)) return;
        }

        // A tight box on the centre. The camera keeps the character there, and
        // everything that went wrong with a wide search went wrong the same
        // way: a totem, a minion and a rare monster all have a green bar, and
        // the only thing that reliably separates yours from theirs is that
        // yours is the one in the middle of the screen.
        var middle = new Rectangle(
            window.X + window.Width * 2 / 5,
            window.Y + window.Height * 3 / 10,
            window.Width / 5,
            window.Height * 2 / 5);

        if (!Scan(middle)) Lost(now);
    }

    private void Lost(long now)
    {
        // A frame or two without it is a flicker, not a disappearance - the bar
        // fades as a character stands still, and blinking the readout in and out
        // would be worse than leaving it.
        if (now - _lastSeenMs > 1500) Visible = false;
    }

    private bool Scan(Rectangle area)
    {
        if (!_cap.Grab(area)) return false;

        byte[] px = _cap.Buffer;
        int w = _cap.Width, h = _cap.Height, stride = w * ScreenCapture.Bpp;

        int cx = w / 2, cy = h / 2;
        Rectangle best = Rectangle.Empty;
        long bestCost = long.MaxValue;

        for (int y = 2; y < h - 2; y++)
        {
            int run = 0;
            for (int x = 0; x <= w; x++)
            {
                bool blue = x < w && IsMana(At(px, stride, x, y));
                if (blue) { run++; continue; }

                if (run >= MinRun
                    && Confirm(px, stride, w, h, x - run, y, run, area, out var hit)
                    && !Ignore.IntersectsWith(hit))
                {
                    int mx = x - run / 2, my = hit.Y - area.Y;
                    long cost = (long)(mx - cx) * (mx - cx) + (long)(my - cy) * (my - cy);
                    if (cost < bestCost) { bestCost = cost; best = hit; }
                }

                run = 0;
            }
        }

        if (best.IsEmpty) return false;

        _last = best;
        Visible = true;
        _lastSeenMs = _clock.ElapsedMilliseconds;
        return true;
    }

    /// <summary>
    /// A blue run is the mana bar if it is thin and framed, or has the life bar
    /// beside it. Water and spell light are neither.
    /// </summary>
    private static bool Confirm(byte[] px, int stride, int w, int h,
                                int x0, int y, int run, Rectangle area, out Rectangle bar)
    {
        bar = Rectangle.Empty;
        int mid = x0 + run / 2;
        if (mid < 0 || mid >= w) return false;

        // How deep the blue goes. A bar is thin; water is not.
        int top = y, bottom = y;
        while (top > 0 && IsMana(At(px, stride, mid, top - 1))) top--;
        while (bottom < h - 1 && IsMana(At(px, stride, mid, bottom + 1))) bottom++;
        if (bottom - top + 1 > MaxThick) return false;

        // The life bar, if it is not covered by the readout deliberately sitting
        // on it. Its absence is not disqualifying for that reason.
        bool life = false;
        for (int dy = 1; dy <= 9 && !life; dy++)
        {
            if (top - dy >= 0 && IsLife(At(px, stride, mid, top - dy))) life = true;
            if (bottom + dy < h && IsLife(At(px, stride, mid, bottom + dy))) life = true;
        }

        bool framed = top - 1 >= 0 && IsDark(At(px, stride, mid, top - 1))
                      || bottom + 1 < h && IsDark(At(px, stride, mid, bottom + 1));

        if (!life && !framed) return false;

        bar = new Rectangle(area.X + x0, area.Y + top, run, bottom - top + 1);
        return true;
    }

    private static (byte R, byte G, byte B) At(byte[] px, int stride, int x, int y)
    {
        int i = y * stride + x * ScreenCapture.Bpp;
        return (px[i + 2], px[i + 1], px[i]);
    }

    private static bool IsLife((byte R, byte G, byte B) c) => IsLife(c.R, c.G, c.B);

    private static bool IsLife(byte r, byte g, byte b) =>
        g > 90 && g > r + 40 && g > b + 40;

    private static bool IsMana((byte R, byte G, byte B) c) =>
        c.B > 85 && c.B > c.R + 28 && c.B > c.G + 12;

    private static bool IsDark((byte R, byte G, byte B) c) =>
        c.R < 70 && c.G < 70 && c.B < 70;

    public void Dispose() => _cap.Dispose();
}
