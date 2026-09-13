namespace P02;

/// <summary>
/// Whether the game is showing its controller HUD or its keyboard HUD.
///
/// The two are not subtly different - they are different layouts. On a pad
/// the flasks and skills move into one framed bar along the bottom centre,
/// with the face buttons drawn in their own colours: green A, blue X, yellow
/// Y, red B. On a keyboard that bar is gone, and the flasks sit in their own
/// box beside the life globe, a red one and a blue one.
///
/// So this reads colours, not letters. The key labels are single characters a
/// few pixels high and the text engine returns nothing for them; four
/// saturated buttons in four known places are not something the game world
/// produces by accident.
///
/// Positions are measured in screen heights from the bottom and from the
/// centre or the left edge, because the HUD scales with the height of the
/// window - which is what lets one set of numbers serve an ultrawide and an
/// ordinary 16:9 screen alike.
/// </summary>
internal static class ModeDetector
{
    public enum Mode { Unknown, Keyboard, Controller }

    /// <summary>Red, green and blue of one pixel, 0-255 each.</summary>
    public delegate (int R, int G, int B) Pixel(int x, int y);

    // Face buttons: across from the centre, and up from the bottom, in heights.
    private static readonly (double Dx, string Want)[] Face =
    [
        (0.0285, "green"),   // A
        (0.0715, "blue"),    // X
        (0.1146, "yellow"),  // Y
        (0.1590, "red"),     // B
    ];
    private const double FaceUp = 0.031;

    // Keyboard flasks: across from the left edge, and up from the bottom.
    private const double RedFlaskX = 0.241, BlueFlaskX = 0.287, FlaskUp = 0.060;

    public static Mode Detect(int width, int height, Pixel at)
    {
        if (width < 200 || height < 200) return Mode.Unknown;

        int faces = 0;
        foreach (var (dx, want) in Face)
        {
            var c = Average(at, width, height,
                            width / 2.0 + dx * height, height - FaceUp * height);
            if (Is(c, want)) faces++;
        }

        // Three of four, so one button darkened by a passing effect does not
        // throw the answer.
        if (faces >= 3) return Mode.Controller;

        var red = Average(at, width, height, RedFlaskX * height, height - FlaskUp * height);
        var blue = Average(at, width, height, BlueFlaskX * height, height - FlaskUp * height);
        if (Is(red, "red") && Is(blue, "blue")) return Mode.Keyboard;

        // Neither: a loading screen, a menu, the passive tree. Nothing is said,
        // and whatever is set stays set.
        return Mode.Unknown;
    }

    /// <summary>The average colour of a small square, to shrug off one odd pixel.</summary>
    private static (int R, int G, int B) Average(Pixel at, int width, int height,
                                                 double cx, double cy)
    {
        int half = Math.Max(2, (int)(height * 0.005));
        long r = 0, g = 0, b = 0, n = 0;

        for (int y = (int)cy - half; y <= (int)cy + half; y++)
        for (int x = (int)cx - half; x <= (int)cx + half; x++)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) continue;
            var p = at(x, y);
            r += p.R; g += p.G; b += p.B; n++;
        }

        return n == 0 ? (0, 0, 0) : ((int)(r / n), (int)(g / n), (int)(b / n));
    }

    // What decides it is one colour standing clear of the other two; the
    // brightness floors only keep plain black from counting. They are low on
    // purpose. The keyboard flask measured (5, 20, 56) with the pause menu up,
    // because the game dims everything behind a menu - still unmistakably blue,
    // and a floor of 60 threw it away.
    private static bool Is((int R, int G, int B) c, string want) => want switch
    {
        "green" => c.G > c.R + 20 && c.G > c.B + 12 && c.G > 35,
        "blue" => c.B > c.R + 25 && c.B > c.G + 5 && c.B > 35,
        "yellow" => c.R > 60 && c.G > 50 && c.B < c.R - 30 && c.B < c.G - 20,
        "red" => c.R > c.G + 35 && c.R > c.B + 35 && c.R > 45,
        _ => false,
    };

    /// <summary>Reads the game window as it is on screen right now.</summary>
    public static Mode DetectNow(Rectangle window)
    {
        using var cap = new ScreenCapture();
        if (!cap.Grab(window)) return Mode.Unknown;

        byte[] px = cap.Buffer;
        int w = cap.Width, h = cap.Height, stride = w * ScreenCapture.Bpp;

        return Detect(w, h, (x, y) =>
        {
            int i = y * stride + x * ScreenCapture.Bpp;
            return (px[i + 2], px[i + 1], px[i]);
        });
    }
}
