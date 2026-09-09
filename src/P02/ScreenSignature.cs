namespace P02;

/// <summary>
/// A handful of pixels that say which layout is on screen.
///
/// Finding the character was the wrong tool for this. He is a small moving
/// thing that the readout is deliberately parked on top of, and every failure
/// to see him became "cannot see your character" - an error about the method,
/// not about anything the player did.
///
/// The panels themselves are the opposite: huge, in fixed places, and either
/// there or not. So this samples a few points down each side of the game window
/// and remembers what they looked like when a spot was saved. Comparing what is
/// there now against those three answers "which layout is this" without needing
/// to find anything at all.
/// </summary>
internal static class ScreenSignature
{
    /// <summary>Where to look, as fractions of the window.</summary>
    private static readonly (double X, double Y)[] Points =
    [
        (0.06, 0.20), (0.06, 0.45), (0.06, 0.70),
        (0.16, 0.20), (0.16, 0.45), (0.16, 0.70),
        (0.84, 0.20), (0.84, 0.45), (0.84, 0.70),
        (0.94, 0.20), (0.94, 0.45), (0.94, 0.70),
    ];

    public static int Length => Points.Length;

    /// <summary>
    /// What those points look like right now, as packed colours.
    ///
    /// One capture of a thin strip down each side rather than twelve tiny ones:
    /// a capture costs about the same whatever its size, and twelve of them
    /// would cost twelve times as much for the same twelve pixels.
    /// </summary>
    public static int[] Sample(Rectangle window)
    {
        var made = new int[Points.Length];
        if (window.Width < 100 || window.Height < 100) return made;

        using var cap = new ScreenCapture();
        if (!cap.Grab(window)) return made;

        byte[] px = cap.Buffer;
        int w = cap.Width, h = cap.Height, stride = w * ScreenCapture.Bpp;

        for (int i = 0; i < Points.Length; i++)
        {
            int x = Math.Clamp((int)(w * Points[i].X), 0, w - 1);
            int y = Math.Clamp((int)(h * Points[i].Y), 0, h - 1);
            int at = y * stride + x * ScreenCapture.Bpp;

            made[i] = px[at + 2] << 16 | px[at + 1] << 8 | px[at];
        }

        return made;
    }

    /// <summary>
    /// How unlike two signatures are, 0 for identical.
    ///
    /// Per channel and averaged, so it is a number anyone can reason about: a
    /// world that has merely moved differs by a few, a panel opening over these
    /// points differs by tens.
    /// </summary>
    public static int Distance(int[] a, int[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return int.MaxValue;

        long total = 0;
        for (int i = 0; i < a.Length; i++)
        {
            total += Math.Abs((a[i] >> 16 & 0xFF) - (b[i] >> 16 & 0xFF));
            total += Math.Abs((a[i] >> 8 & 0xFF) - (b[i] >> 8 & 0xFF));
            total += Math.Abs((a[i] & 0xFF) - (b[i] & 0xFF));
        }

        return (int)(total / (a.Length * 3));
    }
}
