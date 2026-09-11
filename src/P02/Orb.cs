using System.Drawing.Drawing2D;

namespace P02;

/// <summary>
/// The orb, drawn at any size and any moment of its turn.
///
/// An original rendering in the register of the game's currency: a glass
/// sphere with chaotic green-teal energy turning inside it, hot at the centre,
/// cold and deep at the rim. It is not a copy of anyone's artwork - it is
/// generated, here, from geometry - which is why it can be spun, resized and
/// re-lit at will where a copied picture could only be stretched.
///
/// <paramref name="phase"/> is where in the turn it is, in turns: 0 to 1 and
/// round again.
/// </summary>
internal static class Orb
{
    public static Bitmap Render(int size, double phase = 0)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);
        Paint(g, new RectangleF(0, 0, size, size), phase);
        return bmp;
    }

    public static void Paint(Graphics g, RectangleF box, double phase)
    {
        float s = Math.Min(box.Width, box.Height);
        float pad = s * 0.10f;
        var disc = new RectangleF(box.X + pad, box.Y + pad, s - pad * 2, s - pad * 2);
        float cx = disc.X + disc.Width / 2f, cy = disc.Y + disc.Height / 2f;
        float r = disc.Width / 2f;
        double spin = phase * Math.PI * 2;

        // The light it throws, so it sits in the window rather than on it.
        using (var aura = new GraphicsPath())
        {
            // Kept inside the frame. Spilling past it leaves a flat green
            // halo squared off at the edges, because the falloff never reaches
            // nothing before it runs out of picture.
            var wide = RectangleF.Inflate(disc, r * 0.14f, r * 0.14f);
            aura.AddEllipse(wide);
            using var glow = new PathGradientBrush(aura)
            {
                CenterColor = Color.FromArgb(58, 30, 220, 140),
                SurroundColors = [Color.FromArgb(0, 30, 220, 140)],
                CenterPoint = new PointF(cx, cy),
                FocusScales = new PointF(0.74f, 0.74f),
            };
            g.FillEllipse(glow, wide);
        }

        // Deep glass. Almost black where it curves away, so the energy inside
        // has somewhere to be bright against.
        using (var shell = new GraphicsPath())
        {
            shell.AddEllipse(disc);
            using var fill = new PathGradientBrush(shell)
            {
                CenterColor = Color.FromArgb(255, 10, 74, 48),
                SurroundColors = [Color.FromArgb(255, 2, 14, 11)],
                CenterPoint = new PointF(cx - r * 0.12f, cy - r * 0.14f),
                FocusScales = new PointF(0.22f, 0.22f),
            };
            g.FillEllipse(fill, disc);
        }

        var saved = g.Save();
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(disc);
            g.SetClip(clip);

            // The storm, as cloud rather than as line.
            //
            // Arms drawn with a pen read as drawn - they have an edge, and
            // there is no edge in a thing like this. Soft blobs laid along the
            // same spiral, each a gradient fading to nothing, pile up into
            // something that looks stirred instead.
            for (int arm = 0; arm < 3; arm++)
            {
                double turn = spin + arm * Math.PI * 2 / 3;

                for (double t = 0.04; t <= 1.0; t += 0.018)
                {
                    double angle = turn + t * 3.9;
                    double radius = r * (0.08 + 0.90 * Math.Pow(t, 0.78));
                    float px = cx + (float)(Math.Cos(angle) * radius);
                    float py = cy + (float)(Math.Sin(angle) * radius);

                    float puff = r * (0.26f - 0.17f * (float)t);
                    int core = (int)(120 * (1 - t * 0.75));

                    var blob = new RectangleF(px - puff, py - puff, puff * 2, puff * 2);
                    using var path = new GraphicsPath();
                    path.AddEllipse(blob);
                    using var mist = new PathGradientBrush(path)
                    {
                        // Green, not cyan. The blue channel is what turns this
                        // into bathroom glass, so it stays low except right at
                        // the hot middle where everything goes white anyway.
                        CenterColor = Color.FromArgb(
                            Math.Clamp(core, 0, 255),
                            (int)(30 + 150 * (1 - t) * (1 - t)),
                            255,
                            (int)(120 + 90 * (1 - t) * (1 - t))),
                        SurroundColors = [Color.FromArgb(0, 12, 150, 96)],
                    };
                    g.FillEllipse(mist, blob);
                }
            }

            // The eye of it. Small, white, and the only hard light in the glass.
            for (int pass = 0; pass < 2; pass++)
            {
                float k = r * (pass == 0 ? 0.42f : 0.17f);
                var heart = new RectangleF(cx - k, cy - k, k * 2, k * 2);
                using var path = new GraphicsPath();
                path.AddEllipse(heart);
                using var hot = new PathGradientBrush(path)
                {
                    CenterColor = pass == 0
                        ? Color.FromArgb(165, 180, 255, 205)
                        : Color.FromArgb(255, 255, 255, 245),
                    SurroundColors = [Color.FromArgb(0, 60, 255, 170)],
                };
                g.FillEllipse(hot, heart);
            }

            // The glass darkens towards its own edge from the inside, which is
            // most of what makes a circle read as a sphere.
            using (var inner = new GraphicsPath())
            {
                inner.AddEllipse(disc);
                using var edge = new PathGradientBrush(inner)
                {
                    CenterColor = Color.FromArgb(0, 0, 0, 0),
                    SurroundColors = [Color.FromArgb(235, 1, 9, 7)],
                    CenterPoint = new PointF(cx, cy),
                    FocusScales = new PointF(0.62f, 0.62f),
                };
                g.FillEllipse(edge, disc);
            }
        }
        g.Restore(saved);

        // Highlight, up and to the left, as though lit from over your shoulder.
        using (var gloss = new GraphicsPath())
        {
            var spot = new RectangleF(cx - r * 0.62f, cy - r * 0.84f, r * 0.78f, r * 0.52f);
            gloss.AddEllipse(spot);
            using var shine = new PathGradientBrush(gloss)
            {
                CenterColor = Color.FromArgb(150, 255, 255, 255),
                SurroundColors = [Color.FromArgb(0, 255, 255, 255)],
            };
            g.FillEllipse(shine, spot);
        }

        // Bounce along the lower rim, where a sphere catches the surface it
        // sits over.
        using (var bounce = new Pen(Color.FromArgb(70, 110, 255, 190),
                                    Math.Max(1f, s * 0.022f)))
            g.DrawArc(bounce, disc.X + r * 0.09f, disc.Y + r * 0.09f,
                      disc.Width - r * 0.18f, disc.Height - r * 0.18f, 45, 85);

        // The glass itself: a thin cold edge, not a frame around a picture.
        using (var lip = new Pen(Color.FromArgb(80, 120, 255, 195), Math.Max(1f, s * 0.018f)))
            g.DrawEllipse(lip, disc);
    }
}
