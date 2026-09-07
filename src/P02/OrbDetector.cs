namespace P02;

/// <summary>
/// Reads how full a globe is by looking at pixels, not text. The globe drains
/// top-down, so the answer is the height of the liquid surface — measured
/// against calibrated full and empty rows rather than the box edges, because a
/// hand-drawn box always carries some frame with it.
/// </summary>
internal static class OrbDetector
{
    /// <summary>Last auto-find explanation, shown when the search comes up empty.</summary>
    public static string LastLocateNote { get; private set; } = "";

    /// <summary>
    /// Is this pixel the globe's liquid? Uses how far the hue's channel leads
    /// the others in absolute terms, not a ratio: the middle of the mana globe
    /// is a washed-out pale blue where blue barely outweighs green
    /// proportionally, but still leads it by a wide margin.
    /// </summary>
    private static bool IsLiquid(byte b, byte g, byte r,
                                 bool blue, int margin, int minV, bool glare)
    {
        // The specular highlight on the glass is near-white, so it fails every
        // hue test even though it is plainly inside the liquid.
        if (glare && b > 170 && g > 170 && r > 170) return true;

        return blue
            ? b >= minV && b - Math.Max(g, r) >= margin
            : r >= minV && r - Math.Max(g, b) >= margin;
    }

    private static bool RowIsLiquid(byte[] buf, int w, int y, int x0, int x1,
                                    bool blue, WatcherConfig c, int need)
    {
        int rowStart = y * w * ScreenCapture.Bpp;
        int hits = 0;
        for (int x = x0; x < x1; x++)
        {
            int i = rowStart + x * ScreenCapture.Bpp;
            if (IsLiquid(buf[i], buf[i + 1], buf[i + 2],
                         blue, c.ColourMargin, c.MinValue, c.GlareIsLiquid))
            {
                if (++hits >= need) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Row where the liquid surface sits, in box coordinates. Returns
    /// <paramref name="h"/> when the globe reads empty.
    /// </summary>
    public static int SurfaceRow(byte[] buf, int w, int h, WatcherConfig c)
    {
        if (w < 4 || h < 4) return h;
        bool blue = c.Hue.Equals("blue", StringComparison.OrdinalIgnoreCase);

        int band = Math.Max(1, (int)(w * c.BandFraction));
        int x0 = (w - band) / 2;
        int x1 = x0 + band;
        int need = Math.Max(1, (int)Math.Ceiling(band * c.RowThreshold));
        int run = Math.Max(1, c.MinRun);

        int streak = 0;
        for (int y = 0; y < h; y++)
        {
            if (RowIsLiquid(buf, w, y, x0, x1, blue, c, need))
            {
                // The run has to hold, so a spark or a floating damage number
                // cannot fake a full globe.
                if (++streak >= run) return y - run + 1;
            }
            else
            {
                streak = 0;
            }
        }
        return h;
    }

    /// <summary>Fraction still filled, 0-1. Buffer is BGRA, top row first.</summary>
    public static double Fraction(byte[] buf, int w, int h, WatcherConfig c)
        => FractionFromRow(SurfaceRow(buf, w, h, c), h, c);

    /// <summary>
    /// Turns a surface row into a fraction. Without calibration the box edges
    /// are taken to be the globe, which reads low by however much frame the box
    /// caught; with it, full and empty are where you said they were.
    /// </summary>
    public static double FractionFromRow(int surface, int h, WatcherConfig c)
    {
        Span_(h, c, out int top, out int bottom);
        double f = (bottom - surface) / (double)(bottom - top);
        return Math.Clamp(f, 0, 1);
    }

    /// <summary>Where to draw a given fraction, in box coordinates.</summary>
    public static int RowForFraction(double fraction, int h, WatcherConfig c)
    {
        Span_(h, c, out int top, out int bottom);
        return (int)Math.Round(bottom - fraction * (bottom - top));
    }

    private static void Span_(int h, WatcherConfig c, out int top, out int bottom)
    {
        top = c.FullRow;
        bottom = c.EmptyRow;
        if (top < 0 || bottom <= top || bottom > h) { top = 0; bottom = h; }
    }

    /// <summary>
    /// With the globe full, finds the top and bottom of the liquid so later
    /// readings are measured against the globe instead of the box. Returns
    /// false if it cannot see a globe in there at all.
    /// </summary>
    public static bool CalibrateFull(byte[] buf, int w, int h, WatcherConfig c,
                                     out int fullRow, out int emptyRow)
    {
        fullRow = emptyRow = -1;
        if (w < 4 || h < 4) return false;
        bool blue = c.Hue.Equals("blue", StringComparison.OrdinalIgnoreCase);

        int band = Math.Max(1, (int)(w * c.BandFraction));
        int x0 = (w - band) / 2;
        int x1 = x0 + band;
        int need = Math.Max(1, (int)Math.Ceiling(band * c.RowThreshold));

        int first = -1, last = -1;
        for (int y = 0; y < h; y++)
        {
            if (RowIsLiquid(buf, w, y, x0, x1, blue, c, need))
            {
                if (first < 0) first = y;
                last = y;
            }
        }

        if (first < 0 || last - first < 8) return false;
        fullRow = first;
        emptyRow = last + 1;
        return true;
    }

    // ------------------------------------------------------------------
    // learning what full and empty look like
    // ------------------------------------------------------------------

    /// <summary>What the globe's pixels look like over a stretch of rows.</summary>
    public readonly record struct GlobeStats(int DomLow, int DomHigh,
                                             int ValLow, int ValHigh, int Count);

    /// <summary>
    /// Measures colour lead and brightness across the sampled band. Low and
    /// high are the 10th and 90th percentiles, so a few stray pixels — the
    /// rune, a spark — cannot drag the answer around.
    /// </summary>
    public static GlobeStats Measure(byte[] buf, int w, int h, WatcherConfig c,
                                     int rowFrom, int rowTo)
    {
        bool blue = c.Hue.Equals("blue", StringComparison.OrdinalIgnoreCase);
        int band = Math.Max(1, (int)(w * c.BandFraction));
        int x0 = (w - band) / 2;
        int x1 = x0 + band;
        rowFrom = Math.Clamp(rowFrom, 0, h);
        rowTo = Math.Clamp(rowTo, rowFrom, h);

        var domHist = new int[256];
        var valHist = new int[256];
        int n = 0;

        for (int y = rowFrom; y < rowTo; y++)
        {
            int rowStart = y * w * ScreenCapture.Bpp;
            for (int x = x0; x < x1; x++)
            {
                int i = rowStart + x * ScreenCapture.Bpp;
                byte b = buf[i], g = buf[i + 1], r = buf[i + 2];

                // Near-white pixels are the glass highlight and the rune, not
                // the liquid; they say nothing about how full the globe is.
                if (b > 170 && g > 170 && r > 170) continue;

                int dom = blue ? b - Math.Max(g, r) : r - Math.Max(g, b);
                int val = blue ? b : r;
                domHist[Math.Clamp(dom, 0, 255)]++;
                valHist[val]++;
                n++;
            }
        }

        if (n == 0) return new GlobeStats(0, 0, 0, 0, 0);
        return new GlobeStats(Percentile(domHist, n, 0.10), Percentile(domHist, n, 0.90),
                              Percentile(valHist, n, 0.10), Percentile(valHist, n, 0.90), n);
    }

    private static int Percentile(int[] hist, int total, double p)
    {
        int want = (int)(total * p);
        int seen = 0;
        for (int v = 0; v < hist.Length; v++)
        {
            seen += hist[v];
            if (seen >= want) return v;
        }
        return 255;
    }

    /// <summary>
    /// Puts the thresholds between what full looks like and what empty looks
    /// like. Returns false when the two are too alike to tell apart, which is
    /// worth saying out loud rather than silently picking a bad number.
    /// </summary>
    public static bool AutoTune(WatcherConfig c, out string note)
    {
        int domFull = c.FullDominance, valFull = c.FullValue;
        int domEmpty = c.EmptyDominance, valEmpty = c.EmptyValue;

        if (domFull < 0 || domEmpty < 0)
        {
            note = "needs both Full = 100% and Empty = 0%";
            return false;
        }

        int sepDom = domFull - domEmpty;
        int sepVal = valFull - valEmpty;

        // Prefer whichever actually separates. On the life globe the drained
        // part is the same red only darker, so brightness is usually the one
        // that does the work; on mana the colour lead often does.
        if (sepDom < 8 && sepVal < 12)
        {
            note = $"full and empty look too alike here (colour lead {domEmpty}->{domFull}, " +
                   $"brightness {valEmpty}->{valFull}). Is the box mostly globe, and was " +
                   "the globe really drained when you pressed Empty?";
            return false;
        }

        if (sepDom >= 8)
            c.ColourMargin = Math.Clamp(domEmpty + sepDom / 2, 4, 90);
        else
            c.ColourMargin = Math.Clamp(Math.Min(domFull - 2, 12), 4, 90);

        if (sepVal >= 12)
            c.MinValue = Math.Clamp(valEmpty + sepVal / 2, 0, 200);

        note = $"colour lead ≥ {c.ColourMargin}, brightness ≥ {c.MinValue}";
        return true;
    }

    // ------------------------------------------------------------------
    // auto-find
    // ------------------------------------------------------------------

    private readonly record struct Blob(int Count, int X0, int Y0, int X1, int Y1)
    {
        public int W => X1 - X0 + 1;
        public int H => Y1 - Y0 + 1;
        public double Aspect => W / (double)H;

        /// <summary>How much of the bounding box the blob actually fills. A disc
        /// is about 0.79; an L-shaped smear of UI icons is far lower, and a
        /// solid icon is 1.0.</summary>
        public double Density => Count / (double)Math.Max(1, W * H);
    }

    /// <summary>
    /// Finds a globe inside <paramref name="search"/>. Works on connected
    /// regions rather than row/column projections, because the corners of the
    /// HUD also hold skill gems and flasks in the same colours — projecting
    /// merges those into the globe and the result is a box around all of it.
    /// Only meaningful when the globe is full.
    /// </summary>
    public static Rectangle? AutoLocate(Rectangle search, bool blue, int margin, int minV)
    {
        LastLocateNote = "";
        using var cap = new ScreenCapture();
        if (!cap.Grab(search))
        {
            LastLocateNote = "screen capture failed";
            return null;
        }

        var found = Locate(cap.Buffer, cap.Width, cap.Height, blue, margin, minV);
        return found is null
            ? null
            : new Rectangle(search.Left + found.Value.X, search.Top + found.Value.Y,
                            found.Value.Width, found.Value.Height);
    }

    /// <summary>The search itself, over a raw BGRA buffer. Split out from the
    /// capture so it can be exercised without a screen.</summary>
    internal static Rectangle? Locate(byte[] buf, int w, int h, bool blue,
                                      int margin = 30, int minV = 50)
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
                if (IsLiquid(buf[i], buf[i + 1], buf[i + 2], blue, margin, minV, glare: false))
                {
                    mask[y * w + x] = true;
                    lit++;
                }
            }
        }

        if (lit < 200)
        {
            LastLocateNote = $"almost no {(blue ? "blue" : "red")} pixels found — is the " +
                             "game on screen, and is the globe in the corner it should be?";
            return null;
        }

        var blobs = FindBlobs(mask, w, h);
        if (blobs.Count == 0) { LastLocateNote = "no regions found"; return null; }

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
            $"largest region was {best.W}x{best.H}, {best.Density:P0} filled — too small, " +
            "too oblong or too square to be a globe. Try lowering Colour margin.";
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

    /// <summary>Tints every pixel the detector counts as liquid, so a bad read
    /// can be seen rather than guessed at.</summary>
    public static Bitmap MaskOverlay(Bitmap shot, WatcherConfig c)
    {
        bool blue = c.Hue.Equals("blue", StringComparison.OrdinalIgnoreCase);
        var buf = ScreenCapture.ToBuffer(shot);
        var outp = new Bitmap(shot.Width, shot.Height);
        for (int y = 0; y < shot.Height; y++)
        {
            for (int x = 0; x < shot.Width; x++)
            {
                int i = (y * shot.Width + x) * ScreenCapture.Bpp;
                byte b = buf[i], g = buf[i + 1], r = buf[i + 2];
                bool on = IsLiquid(b, g, r, blue, c.ColourMargin, c.MinValue, c.GlareIsLiquid);
                outp.SetPixel(x, y, on
                    ? Color.FromArgb(0, 255, 0)
                    : Color.FromArgb(r / 3, g / 3, b / 3));
            }
        }
        return outp;
    }
}
