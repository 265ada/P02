using System.Drawing.Drawing2D;

namespace P02;

/// <summary>
/// What sits behind the window.
///
/// Drawn rather than shipped. The obvious way to give this the look of the game
/// is to use the game's own art, and that art belongs to the people who made
/// it - so what is here is an original scene in the same register: a dark
/// vault, embers coming up from below, and a slow arcane ring turning behind
/// the panels.
///
/// It is deliberately quiet. The cards sit on top of it at full opacity, so
/// only the margins show - which is where flair belongs in something read
/// mid-fight. Anything busier would be competing with the numbers.
///
/// If you would rather have your own picture behind it, drop a PNG or JPG at
/// %APPDATA%\P02\backdrop.png and it is used instead, dimmed enough to keep
/// the text on top of it readable.
/// </summary>
internal static class Backdrop
{
    private static Bitmap? _cache;
    private static Size _for;
    private static bool _lookedForFile;
    private static Image? _yours;

    public static void Draw(Graphics g, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0) return;

        // Never rendered while the window is being dragged about.
        //
        // Drawing this is a few hundred gradient fills, and Windows asks for a
        // repaint on every pixel of a resize. Re-rendering each time made
        // dragging an edge feel like wading. The one already in hand is
        // stretched to fit instead - nobody can see the difference in a window
        // that is moving - and the real one is drawn again once somebody has
        // let go and asked for it with Forget.
        if (_cache is null)
        {
            _cache = Render(area.Size);
            _for = area.Size;
        }

        if (_for == area.Size)
        {
            g.DrawImageUnscaled(_cache, 0, 0);
            return;
        }

        var was = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_cache, 0, 0, area.Width, area.Height);
        g.InterpolationMode = was;
    }

    /// <summary>Throws the cached picture away, so a new one is drawn next paint.</summary>
    public static void Forget()
    {
        _cache?.Dispose();
        _cache = null;
        _lookedForFile = false;
        _yours?.Dispose();
        _yours = null;
    }

    private static Bitmap Render(Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var all = new Rectangle(0, 0, size.Width, size.Height);

        if (Yours() is { } picture)
        {
            // Cover, not stretch: a photograph squashed to fit reads as a
            // mistake even when nobody can say why.
            double scale = Math.Max(size.Width / (double)picture.Width,
                                    size.Height / (double)picture.Height);
            int w = (int)(picture.Width * scale), h = (int)(picture.Height * scale);
            g.DrawImage(picture, (size.Width - w) / 2, (size.Height - h) / 2, w, h);

            // Dimmed hard. Whatever it is, the numbers on top of it have to
            // stay readable, and that matters more than the picture does.
            using var scrim = new SolidBrush(Color.FromArgb(196, Theme.Bg));
            g.FillRectangle(scrim, all);
            return bmp;
        }

        // The vault: near-black at the top, warmer towards the floor.
        using (var deep = new LinearGradientBrush(all,
                   Color.FromArgb(255, 14, 12, 12),
                   Color.FromArgb(255, 34, 24, 19),
                   LinearGradientMode.Vertical))
            g.FillRectangle(deep, all);

        // Two rings, off-centre and barely there, turning behind everything.
        var heart = new PointF(size.Width * 0.5f, size.Height * 0.62f);
        for (int ring = 0; ring < 2; ring++)
        {
            float radius = size.Height * (ring == 0 ? 0.52f : 0.34f);
            using var pen = new Pen(Color.FromArgb(ring == 0 ? 16 : 22, Theme.Accent),
                                    ring == 0 ? 2.5f : 1.5f);
            g.DrawEllipse(pen, heart.X - radius, heart.Y - radius, radius * 2, radius * 2);

            // Ticks around it, like a dial nobody set.
            int ticks = ring == 0 ? 48 : 24;
            for (int i = 0; i < ticks; i++)
            {
                double a = i * Math.PI * 2 / ticks + ring * 0.13;
                float inner = radius * 0.965f, outer = radius * 1.02f;
                using var mark = new Pen(Color.FromArgb(i % 4 == 0 ? 30 : 14, Theme.Accent), 1.4f);
                g.DrawLine(mark,
                    heart.X + (float)(Math.Cos(a) * inner), heart.Y + (float)(Math.Sin(a) * inner),
                    heart.X + (float)(Math.Cos(a) * outer), heart.Y + (float)(Math.Sin(a) * outer));
            }
        }

        // Embers, rising and fading. Seeded, so the window does not shuffle
        // itself every time it is resized.
        var rng = new Random(20260911);
        for (int i = 0; i < 130; i++)
        {
            float x = (float)rng.NextDouble() * size.Width;
            float up = (float)Math.Pow(rng.NextDouble(), 1.7);
            float y = size.Height - up * size.Height * 0.95f;
            float dot = 1.1f + (float)rng.NextDouble() * 2.3f * (1 - up * 0.6f);
            int glow = (int)(150 * (1 - up) * (0.35 + rng.NextDouble() * 0.65));

            using var brush = new SolidBrush(Color.FromArgb(
                Math.Clamp(glow, 0, 255), 236, 154 + rng.Next(50), 74));
            g.FillEllipse(brush, x, y, dot, dot);

            if (dot > 2.4f)
            {
                using var halo = new SolidBrush(Color.FromArgb(
                    Math.Clamp(glow / 4, 0, 60), 236, 160, 80));
                g.FillEllipse(halo, x - dot, y - dot, dot * 3, dot * 3);
            }
        }

        // Forge-light from the bottom corners, where the globes are on screen.
        foreach (var (fx, tint) in new[]
                 { (0.10f, Color.FromArgb(168, 46, 44)), (0.90f, Color.FromArgb(52, 96, 168)) })
        {
            float radius = size.Height * 0.75f;
            var spot = new RectangleF(size.Width * fx - radius, size.Height - radius * 0.55f,
                                      radius * 2, radius * 1.4f);
            using var path = new GraphicsPath();
            path.AddEllipse(spot);
            using var glow = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(34, tint),
                SurroundColors = [Color.FromArgb(0, tint)],
            };
            g.FillEllipse(glow, spot);
        }

        // A vignette, so the eye goes to the middle where the work is.
        using (var edge = new GraphicsPath())
        {
            var wide = new Rectangle(-size.Width / 3, -size.Height / 3,
                                     size.Width * 5 / 3, size.Height * 5 / 3);
            edge.AddEllipse(wide);
            using var dark = new PathGradientBrush(edge)
            {
                CenterColor = Color.FromArgb(0, 0, 0, 0),
                SurroundColors = [Color.FromArgb(190, 0, 0, 0)],
                CenterPoint = new PointF(size.Width / 2f, size.Height / 2f),
                FocusScales = new PointF(0.55f, 0.45f),
            };
            g.FillRectangle(dark, all);
        }

        return bmp;
    }

    private static Image? Yours()
    {
        if (_lookedForFile) return _yours;
        _lookedForFile = true;

        foreach (string name in new[] { "backdrop.png", "backdrop.jpg", "backdrop.jpeg" })
        {
            string path = Path.Combine(AppConfig.Dir, name);
            if (!File.Exists(path)) continue;
            try
            {
                // Read through a copy, or the file stays locked for as long as
                // the app runs and cannot be replaced without closing it.
                using var raw = File.OpenRead(path);
                using var loaded = Image.FromStream(raw);
                _yours = new Bitmap(loaded);
                Log.Write($"backdrop: using your own picture from {path}");
                return _yours;
            }
            catch (Exception ex)
            {
                Log.Write($"backdrop: could not read {path} - {ex.Message}");
            }
        }

        return null;
    }
}
