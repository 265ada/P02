using P02;

static class T
{
    const int W = 700, H = 460, Bpp = 4;
    static byte[] _buf = new byte[W * H * Bpp];

    static void Px(int x, int y, int b, int g, int r)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = (y * W + x) * Bpp;
        _buf[i] = (byte)b; _buf[i + 1] = (byte)g; _buf[i + 2] = (byte)r; _buf[i + 3] = 255;
    }

    static void Disc(int cx, int cy, int rad, int b, int g, int r)
    {
        for (int y = cy - rad; y <= cy + rad; y++)
            for (int x = cx - rad; x <= cx + rad; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad) Px(x, y, b, g, r);
    }

    static void Rect(int x0, int y0, int w, int h, int b, int g, int r)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++) Px(x, y, b, g, r);
    }

    static void Reset()
    {
        for (int i = 0; i < _buf.Length; i += Bpp)
        { _buf[i] = 40; _buf[i + 1] = 38; _buf[i + 2] = 36; _buf[i + 3] = 255; }
    }

    static void Main()
    {
        int fails = 0;

        // The real bottom-right corner: mana globe, plus blue skill gems and a
        // blue flask to its left. Projections merged all of this; blobs must not.
        Reset();
        Disc(520, 300, 110, 190, 90, 40);              // mana globe
        Rect(60, 330, 44, 44, 200, 120, 60);           // gem icon
        Rect(112, 330, 44, 44, 200, 120, 60);          // gem icon
        Rect(164, 330, 44, 44, 200, 120, 60);          // gem icon
        Rect(30, 250, 26, 70, 210, 110, 50);           // flask
        var got = OrbDetector.Locate(_buf, W, H, blue: true);
        fails += Check("mana globe beside gems+flask", got, 520, 300, 110);

        // Specular highlight punching a hole in the middle of the globe.
        Reset();
        Disc(520, 300, 110, 190, 90, 40);
        Rect(470, 230, 60, 18, 250, 250, 250);
        got = OrbDetector.Locate(_buf, W, H, blue: true);
        fails += Check("globe with glare streak", got, 520, 300, 110);

        // Life corner, red.
        Reset();
        Disc(150, 300, 100, 40, 40, 180);
        Rect(300, 300, 120, 20, 50, 50, 190);          // a red bar, not round
        got = OrbDetector.Locate(_buf, W, H, blue: false);
        fails += Check("life globe beside a red bar", got, 150, 300, 100);

        // Nothing but icons: must refuse rather than box the icons.
        Reset();
        Rect(60, 330, 44, 44, 200, 120, 60);
        Rect(112, 330, 44, 44, 200, 120, 60);
        got = OrbDetector.Locate(_buf, W, H, blue: true);
        if (got is null) Console.WriteLine("PASS  icons only -> refused");
        else { Console.WriteLine($"FAIL  icons only -> returned {got}"); fails++; }

        // The mana globe's centre is a pale washed-out blue: blue leads green
        // by a wide absolute margin but barely at all proportionally, which is
        // what defeated the old ratio test.
        Reset();
        Disc(520, 300, 110, 150, 70, 35);
        Disc(520, 300, 60, 210, 165, 130);
        got = OrbDetector.Locate(_buf, W, H, blue: true);
        fails += Check("globe with pale washed centre", got, 520, 300, 110);

        // Fill fraction across levels, on a real disc.
        var cfg = new WatcherConfig { Hue = "blue", ColourMargin = 30, MinValue = 50 };
        foreach (double want in new[] { 1.0, 0.75, 0.5, 0.25 })
        {
            Reset();
            int cx = 520, cy = 300, rad = 110;
            int top = (int)(cy - rad + (1 - want) * rad * 2);
            for (int y = top; y <= cy + rad; y++)
                for (int x = cx - rad; x <= cx + rad; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rad * rad)
                        Px(x, y, 190, 90, 40);

            var box = new byte[(rad * 2 + 1) * (rad * 2 + 1) * Bpp];
            int bw = rad * 2 + 1;
            for (int y = 0; y < bw; y++)
                for (int x = 0; x < bw; x++)
                {
                    int src = ((cy - rad + y) * W + (cx - rad + x)) * Bpp;
                    int dst = (y * bw + x) * Bpp;
                    Array.Copy(_buf, src, box, dst, Bpp);
                }
            double f = OrbDetector.Fraction(box, bw, bw, cfg);
            bool ok = Math.Abs(f - want) < 0.04;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  fill {want:P0} -> read {f:P1}");
            if (!ok) fails++;
        }

        // A box drawn by hand catches frame above the globe. Uncalibrated that
        // reads low; calibration must pull a full globe back to a true 100%.
        {
            const int pad = 24, bw2 = 220, bh2 = 270;
            var box = new byte[bw2 * bh2 * Bpp];
            for (int i = 0; i < box.Length; i += Bpp)
            { box[i] = 40; box[i + 1] = 38; box[i + 2] = 36; box[i + 3] = 255; }

            int rad = 105, cx = bw2 / 2, cy = pad + rad;
            for (int yy = 0; yy < bh2; yy++)
                for (int xx = 0; xx < bw2; xx++)
                    if ((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy) <= rad * rad)
                    {
                        int i = (yy * bw2 + xx) * Bpp;
                        box[i] = 190; box[i + 1] = 90; box[i + 2] = 40;
                    }

            var c2 = new WatcherConfig { Hue = "blue", ColourMargin = 30, MinValue = 50 };
            double before = OrbDetector.Fraction(box, bw2, bh2, c2);
            bool okCal = OrbDetector.CalibrateFull(box, bw2, bh2, c2, out int fr, out int er);
            c2.FullRow = fr;
            c2.EmptyRow = er;
            double after = OrbDetector.Fraction(box, bw2, bh2, c2);

            bool low = before < 0.95;
            bool fixedUp = okCal && after > 0.99;
            Console.WriteLine((low ? "PASS" : "FAIL") + $"  padded box reads low uncalibrated -> {before:P1}");
            Console.WriteLine((fixedUp ? "PASS" : "FAIL") + $"  calibration restores full -> {after:P1} (rows {fr}..{er})");
            if (!low) fails++;
            if (!fixedUp) fails++;
        }

        Console.WriteLine(fails == 0 ? "\nALL PASS" : $"\n{fails} FAILED");
        Environment.Exit(fails == 0 ? 0 : 1);
    }

    static int Check(string name, Rectangle? got, int cx, int cy, int rad)
    {
        if (got is null) { Console.WriteLine($"FAIL  {name} -> not found"); return 1; }
        var r = got.Value;
        int gx = r.X + r.Width / 2, gy = r.Y + r.Height / 2;
        bool ok = Math.Abs(gx - cx) <= 6 && Math.Abs(gy - cy) <= 6
                  && Math.Abs(r.Width - rad * 2) <= 8 && Math.Abs(r.Height - rad * 2) <= 8;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name} -> {r}");
        return ok ? 0 : 1;
    }
}
