namespace P02;

/// <summary>
/// Reads how full a globe is by looking at pixels, not text. The globe drains
/// top-down, so the answer is the height of the highest run of liquid-coloured
/// rows in a band of central columns.
/// </summary>
internal static class OrbDetector
{
    /// <summary>Last auto-find explanation, shown when the search comes up empty.</summary>
    public static string LastLocateNote { get; private set; } = "";

    private static bool IsLiquid(byte b, byte g, byte r,
                                 bool blue, double ratio, int minV, bool glare)
    {
        // The specular highlight on the glass is near-white, so it fails the
        // hue test even though it is plainly inside the liquid.
        if (glare && b > 170 && g > 170 && r > 170) return true;

        return blue
            ? b > minV && b > g * ratio && b > r * ratio
            : r > minV && r > g * ratio && r > b * ratio;
    }

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
                if (IsLiquid(buf[i], buf[i + 1], buf[i + 2],
                             blue, c.ChannelRatio, c.MinValue, c.GlareIsLiquid))
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

    // ------------------------------------------------------------------
    // auto-find
    // ------------------------------------------------------------------

    private readonly record struct Blob(int Count, int X0, int Y0, int X1, int Y1)
    {
        public int W => X1 - X0 + 1;
        public int H => Y1 - Y0 + 1;
        public double Aspect => W / (double)H;
        /// <summary>How much of the bounding box the blob actually fills. A disc
        /// is about 0.79; an L-shaped smear of UI icons is far lower.</summary>
        public double Density => Count / (double)Math.Max(1, W * H);
    }

    /// <summary>
    /// Finds a globe inside <paramref name="search"/>. Works on connected
    /// regions rather than row/column projections, because the corners of the
    /// HUD also hold skill gems and flasks in the same colours — projecting
    /// merges those into the globe and the result is a box around all of it.
    /// Only meaningful when the globe is full, which is why the UI says to top
    /// up first.
    /// </summary>
    public static Rectangle? AutoLocate(Rectangle search, bool blue,
                                        double ratio = 1.35, int minV = 50)
    {
        LastLocateNote = "";
        using var cap = new ScreenCapture();
        if (!cap.Grab(search))
        {
            LastLocateNote = "screen capture failed";
            return null;
        }

        var found = Locate(cap.Buffer, cap.Width, cap.Height, blue, ratio, minV);
        return found is null
            ? null
            : new Rectangle(search.Left + found.Value.X, search.Top + found.Value.Y,
                            found.Value.Width, found.Value.Height);
    }

    /// <summary>The search itself, over a raw BGRA buffer. Split out from the
    /// capture so it can be exercised without a screen.</summary>
    internal static Rectangle? Locate(byte[] buf, int w, int h, bool blue,
                                      double ratio = 1.35, int minV = 50)
    {
        var mask = new bool[w * h];
        int lit = 0;

        for (int y = 0; y < h; y++)
        {
            int rowStart = y * w * ScreenCapture.Bpp;
            for (int x = 0; x < w; x++)
            {
                int i = rowStart + x * ScreenCapture.Bpp;
                // Glare is excluded here: white pixels are everywhere on the
                // desktop, and we are hunting for the globe's own colour.
                if (IsLiquid(buf[i], buf[i + 1], buf[i + 2], blue, ratio, minV, glare: false))
                {
                    mask[y * w + x] = true;
                    lit++;
                }
            }
        }

        if (lit < 200)
        {
            LastLocateNote = $"almost no {(blue ? "blue" : "red")} pixels found " +
                             "— is the game on screen, in the corner it should be?";
            return null;
        }

        var blobs = FindBlobs(mask, w, h);
        if (blobs.Count == 0) { LastLocateNote = "no regions found"; return null; }

        // A globe is a big round disc. Icons and flasks are small, oblong, or
        // sparse; the fill test throws out anything that is not disc-shaped.
        var ranked = blobs.OrderByDescending(b => b.Count).ToList();
        foreach (var b in ranked)
        {
            // A disc fills about 79% of its bounding box. Much less is an
            // L-shaped smear of UI; much more is a solid rectangle, i.e. an
            // icon. Globes are also far bigger than any icon.
            if (b.W >= 60 && b.H >= 60 &&
                b.Aspect is >= 0.55 and <= 1.8 &&
                b.Density is >= 0.45 and <= 0.92)
            {
                return new Rectangle(b.X0, b.Y0, b.W, b.H);
            }
        }

        var best = ranked[0];
        LastLocateNote =
            $"largest region was {best.W}x{best.H}, {best.Density:P0} filled — " +
            "too small, too oblong or too square to be a globe";
        return null;
    }

    /// <summary>4-connected regions, found iteratively so a big mask cannot
    /// blow the stack.</summary>
    private static List<Blob> FindBlobs(bool[] mask, int w, int h)
    {
        var blobs = new List<Blob>();
        var stack = new Stack<int>();

        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start]) continue;

            int count = 0, x0 = w, y0 = h, x1 = 0, y1 = 0;
            stack.Push(start);
            mask[start] = false;

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % w, py = p / w;
                count++;
                if (px < x0) x0 = px;
                if (px > x1) x1 = px;
                if (py < y0) y0 = py;
                if (py > y1) y1 = py;

                if (px > 0 && mask[p - 1]) { mask[p - 1] = false; stack.Push(p - 1); }
                if (px < w - 1 && mask[p + 1]) { mask[p + 1] = false; stack.Push(p + 1); }
                if (py > 0 && mask[p - w]) { mask[p - w] = false; stack.Push(p - w); }
                if (py < h - 1 && mask[p + w]) { mask[p + w] = false; stack.Push(p + w); }
            }

            if (count >= 400) blobs.Add(new Blob(count, x0, y0, x1, y1));
        }
        return blobs;
    }
}
