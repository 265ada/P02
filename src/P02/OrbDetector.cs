namespace P02;

/// <summary>
/// Reads how full a globe is by looking at pixels, not text. The globe drains
/// top-down, so the answer is the height of the highest run of liquid-coloured
/// rows in a band of central columns.
/// </summary>
internal static class OrbDetector
{
    private static bool IsLiquid(byte b, byte g, byte r, bool blue, double ratio, int minV)
        => blue
            ? b > minV && b > g * ratio && b > r * ratio
            : r > minV && r > g * ratio && r > b * ratio;

    /// <summary>Fraction still filled, 0-1. Buffer is BGRA, top row first.</summary>
    public static double Fraction(byte[] buf, int w, int h, WatcherConfig c)
    {
        if (w < 4 || h < 4) return 0;
        bool blue = c.Hue.Equals("blue", StringComparison.OrdinalIgnoreCase);

        int band = Math.Max(1, (int)(w * c.BandFraction));
        int x0 = (w - band) / 2;
        int x1 = x0 + band;
        int need = Math.Max(1, (int)Math.Ceiling(band * c.RowThreshold));
        int run = Math.Max(1, c.MinRun);

        int streak = 0;
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * ScreenCapture.Bpp;
            int hits = 0;
            for (int x = x0; x < x1; x++)
            {
                int i = rowStart + x * ScreenCapture.Bpp;
                if (IsLiquid(buf[i], buf[i + 1], buf[i + 2], blue, c.ChannelRatio, c.MinValue))
                    hits++;
            }

            if (hits >= need)
            {
                streak++;
                // The run has to hold, so a spark or a floating number can't
                // fake a full globe.
                if (streak >= run)
                {
                    int top = y - run + 1;
                    return (h - top) / (double)h;
                }
            }
            else
            {
                streak = 0;
            }
        }
        return 0;
    }

    /// <summary>Row index where the liquid surface sits, for the preview.</summary>
    public static int FillLine(double fraction, int h) => (int)Math.Round((1 - fraction) * h);

    /// <summary>
    /// Finds a globe inside <paramref name="search"/> by looking for the mass of
    /// liquid-coloured pixels. Only meaningful when the globe is full, which is
    /// why the UI tells you to top up before pressing the button.
    /// </summary>
    public static Rectangle? AutoLocate(Rectangle search, bool blue,
                                        double ratio = 1.35, int minV = 50)
    {
        using var cap = new ScreenCapture();
        if (!cap.Grab(search)) return null;

        int w = cap.Width, h = cap.Height;
        var buf = cap.Buffer;
        var colCount = new int[w];
        var rowCount = new int[h];

        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * ScreenCapture.Bpp;
            for (int x = 0; x < w; x++)
            {
                int i = rowStart + x * ScreenCapture.Bpp;
                if (IsLiquid(buf[i], buf[i + 1], buf[i + 2], blue, ratio, minV))
                {
                    colCount[x]++;
                    rowCount[y]++;
                }
            }
        }

        if (!Span(colCount, out int cx0, out int cx1)) return null;
        if (!Span(rowCount, out int ry0, out int ry1)) return null;

        int rw = cx1 - cx0 + 1, rh = ry1 - ry0 + 1;
        // A globe is round; anything long and thin is a health bar on a mob,
        // a spell effect, or UI chrome.
        double aspect = rw / (double)rh;
        if (rw < 24 || rh < 24 || aspect < 0.6 || aspect > 1.7) return null;

        return new Rectangle(search.Left + cx0, search.Top + ry0, rw, rh);
    }

    /// <summary>Contiguous span around the histogram peak, ignoring stray pixels.</summary>
    private static bool Span(int[] counts, out int lo, out int hi)
    {
        lo = hi = 0;
        int peak = 0, peakIdx = -1;
        for (int i = 0; i < counts.Length; i++)
            if (counts[i] > peak) { peak = counts[i]; peakIdx = i; }
        if (peakIdx < 0 || peak < 8) return false;

        int cut = Math.Max(2, peak / 5);
        lo = hi = peakIdx;
        while (lo > 0 && counts[lo - 1] >= cut) lo--;
        while (hi < counts.Length - 1 && counts[hi + 1] >= cut) hi++;
        return true;
    }
}
