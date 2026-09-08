using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace P02;

/// <summary>
/// Reads the "1,465/1,465" text next to a globe.
///
/// This is a better answer than pixels wherever it works: it gives an exact
/// ratio with no region hunting, no colour thresholds and no calibration, and
/// because it reads the maximum as well, gear and buffs that move your pool
/// change nothing.
///
/// Windows has an OCR engine built in, so this costs no extra binaries. It is
/// not free of mistakes though - "747/747" has come back as "1747/747", and a
/// box spanning two lines once turned a life of 1,465 and a shield of 2,005
/// into a maximum of 14,652,005 - so every reading is checked before it is
/// believed, and the pixels are kept as a second opinion.
/// </summary>
internal sealed partial class TextOcr : IDisposable
{
    public readonly record struct Reading(double Fraction, int Current, int Max,
                                          long AtMs, string Raw);

    // No whitespace inside a number. Allowing it glued the life and shield
    // lines together when the box spanned both: "1,465" and "2,005" became
    // "14,652,005", and a max of fourteen million reads as 0% life.
    [GeneratedRegex(@"([0-9][0-9.,]*)\s*/\s*([0-9][0-9.,]*)")]
    private static partial Regex PairPattern();

    private sealed class Slot
    {
        public Rectangle Region;
        public string Label = "";
        public int ExpectedMax;
        public readonly ScreenCapture Cap = new();
        public Reading Last;
        public bool HasLast;
        public int StableMax;
        public int PendingMax;
        public int PendingCount;
        public long LastComplaintMs = long.MinValue / 2;
        public int DisagreeMax;
        public int DisagreeCount;
        public int Suggested;
    }

    private readonly Dictionary<string, Slot> _slots = new();
    private readonly object _gate = new();
    private readonly OcrEngine? _engine;
    private readonly Thread? _thread;
    private readonly CancellationTokenSource _stop = new();
    private readonly System.Diagnostics.Stopwatch _clock =
        System.Diagnostics.Stopwatch.StartNew();

    public bool Available => _engine is not null;

    /// <summary>Why OCR is unavailable, when it is.</summary>
    public string Unavailable { get; } = "";

    public TextOcr()
    {
        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages()
                      ?? OcrEngine.TryCreateFromLanguage(
                          new Windows.Globalization.Language("en-US"));
        }
        catch (Exception ex)
        {
            Unavailable = ex.Message;
        }

        if (_engine is null)
        {
            if (Unavailable.Length == 0)
                Unavailable = "Windows has no OCR language pack installed.";
            Log.Write($"text reading unavailable: {Unavailable}");
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "P02 ocr" };
        _thread.Start();
    }

    /// <summary>Sets, or with null clears, the text region for a globe.</summary>
    public void Configure(string name, Rectangle? region, string label = "", int expectedMax = 0)
    {
        lock (_gate)
        {
            if (region is null)
            {
                if (_slots.Remove(name, out var gone)) gone.Cap.Dispose();
                return;
            }
            if (!_slots.TryGetValue(name, out var slot))
            {
                slot = new Slot();
                _slots[name] = slot;
            }
            slot.Region = region.Value;
            slot.Label = label;
            slot.ExpectedMax = expectedMax;
        }
    }

    /// <summary>Most recent believable reading, if there is one.</summary>
    public bool TryGet(string name, out Reading reading)
    {
        lock (_gate)
        {
            if (_slots.TryGetValue(name, out var slot) && slot.HasLast)
            {
                reading = slot.Last;
                return true;
            }
        }
        reading = default;
        return false;
    }

    /// <summary>The maximum currently being read and settled on, or 0.</summary>
    public int StableMaxOf(string name)
    {
        lock (_gate)
            return _slots.TryGetValue(name, out var slot) ? slot.StableMax : 0;
    }

    /// <summary>
    /// A maximum that keeps being read while a different one is configured, or
    /// 0 when there is no such disagreement.
    /// </summary>
    public int SuggestedMax(string name)
    {
        lock (_gate)
            return _slots.TryGetValue(name, out var slot) ? slot.Suggested : 0;
    }

    public long NowMs => _clock.ElapsedMilliseconds;

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                KeyValuePair<string, Slot>[] slots;
                lock (_gate) slots = _slots.ToArray();

                foreach (var (name, slot) in slots)
                {
                    if (_stop.IsCancellationRequested) break;
                    ReadOne(name, slot);
                }
            }
            catch (Exception ex)
            {
                Log.Write($"ocr loop: {ex.Message}");
            }

            // Six times a second is plenty: this corrects and confirms the
            // pixel reading, which runs far faster.
            _stop.Token.WaitHandle.WaitOne(160);
        }
    }

    private void ReadOne(string name, Slot slot)
    {
        Rectangle r;
        lock (_gate) r = slot.Region;
        if (r.Width < 8 || r.Height < 4) return;
        if (!slot.Cap.Grab(r)) return;

        string text;
        using (var shot = ToBitmap(slot.Cap.Buffer, slot.Cap.Width, slot.Cap.Height))
        using (var big = Upscale(shot, 3))
            text = Recognise(big);

        if (text.Length == 0) return;

        string label;
        int expected;
        lock (_gate) { label = slot.Label; expected = slot.ExpectedMax; }

        if (!TryParse(text, out int cur, out int max, label, expected))
        {
            // Silence here is correct - a refused reading is better than the
            // wrong line - but it should be explicable.
            if (label.Length > 0
                && !text.Contains(label, StringComparison.OrdinalIgnoreCase)
                && _clock.ElapsedMilliseconds - slot.LastComplaintMs > 10000)
            {
                slot.LastComplaintMs = _clock.ElapsedMilliseconds;
                string seen = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
                Log.Write($"{name}: read \"{seen}\" but it does not contain "
                          + $"\"{label}\" - include that word in the box");
            }
            return;
        }

        // If you have told us the maximum, anything else is a misread. This is
        // the one that matters: a stray leading digit turns 1,465 into 11,465,
        // and a current of 1,465 against that reads as 13% - well under any
        // trigger, so it fires while you are at full health.
        if (expected > 0 && max != expected)
        {
            // Maxima do change - a level, a gear swap - and a stated one that
            // has gone stale refuses every reading in silence, which looks
            // exactly like the app being broken. A misread does not repeat this
            // consistently, so a value that keeps coming back is worth pointing
            // out. It is suggested, never adopted: quietly overriding the
            // number you typed would defeat the check it exists to perform.
            if (max == slot.DisagreeMax) slot.DisagreeCount++;
            else { slot.DisagreeMax = max; slot.DisagreeCount = 1; }

            if (slot.DisagreeCount >= 15)
            {
                lock (_gate) slot.Suggested = max;
                if (_clock.ElapsedMilliseconds - slot.LastComplaintMs > 30000)
                {
                    slot.LastComplaintMs = _clock.ElapsedMilliseconds;
                    Log.Write($"{name}: has read a maximum of {max} {slot.DisagreeCount} times "
                              + $"running but yours is set to {expected} - has it changed?");
                }
            }
            return;
        }

        slot.DisagreeCount = 0;
        lock (_gate) slot.Suggested = 0;

        // Without a stated maximum, lean on the fact that a real one barely
        // ever changes. A small change might be a gear swap; a large one is
        // almost always a digit that was not there, so make it prove itself.
        if (max != slot.StableMax)
        {
            bool big = slot.StableMax > 0
                       && Math.Abs(max - slot.StableMax) > slot.StableMax / 5;

            if (max == slot.PendingMax) slot.PendingCount++;
            else { slot.PendingMax = max; slot.PendingCount = 1; }

            if (slot.PendingCount < (big ? 5 : 2)) return;
            if (big)
                Log.Write($"{name}: maximum changed from {slot.StableMax} to {max}");
            slot.StableMax = max;
        }

        lock (_gate)
        {
            // Clamped for the decision, raw numbers kept for display.
            slot.Last = new Reading(Math.Clamp(cur / (double)max, 0, 1), cur, max,
                                    _clock.ElapsedMilliseconds,
                                    text.Trim());
            slot.HasLast = true;
        }
    }

    /// <summary>
    /// Pulls "current/maximum" out of whatever the engine returned.
    ///
    /// Matching happens inside a line and never across one: a box that also
    /// catches the shield or ward line must not have their numbers combined.
    /// That is not hypothetical - it produced a maximum of 14,652,005 from a
    /// life of 1,465 and a shield of 2,005, which reads as 0% life.
    /// </summary>
    internal static bool TryParse(string text, out int cur, out int max,
                                  string label = "", int expectedMax = 0)
    {
        cur = max = 0;
        var breaks = new[] { '\n', '\r' };
        var lines = text.Split(breaks, StringSplitOptions.RemoveEmptyEntries);

        // A box around the life numbers usually catches shield and ward too,
        // and those are pairs as well - ward at 90/90 read as life is a misfire
        // waiting to happen. So the line is picked deliberately rather than
        // taken as whichever came first.
        //
        // The word beside the numbers is the surest anchor there is, and your
        // own maximum is the next surest. Only failing both does position
        // decide anything.
        var strategies = new Func<string, bool>[]
        {
            l => label.Length > 0 && l.Contains(label, StringComparison.OrdinalIgnoreCase),
            l => expectedMax > 0 && LineMax(l) == expectedMax,
            _ => label.Length == 0 && expectedMax == 0,
        };

        foreach (var pick in strategies)
        {
            foreach (string line in lines)
            {
                if (!pick(line)) continue;
                var m = PairPattern().Match(line);
                if (!m.Success) continue;
                if (TryNumber(m.Groups[1].Value, out cur)
                    && TryNumber(m.Groups[2].Value, out max)
                    && Sane(cur, max)
                    // A label can find the right line and still carry a misread
                    // maximum. If you have said what yours is, anything else is
                    // wrong however convincing the line looked.
                    && (expectedMax <= 0 || max == expectedMax))
                    return true;
            }
        }

        cur = max = 0;
        return false;
    }

    private static int LineMax(string line)
    {
        var m = PairPattern().Match(line);
        return m.Success && TryNumber(m.Groups[2].Value, out int v) ? v : 0;
    }

    /// <summary>
    /// Current above maximum is legitimate in this game - skills can push life
    /// past the pool - so that is not a rejection. What is rejected is a pool
    /// of a size no character has, which is what a misread produces when it
    /// glues two lines together.
    ///
    /// A current value that is too high needs no special handling either way:
    /// above maximum means above full, and above full never fires, so a genuine
    /// overheal and a misread current lead to the same decision. The pixels are
    /// what catch a misread that matters, by disagreeing with it.
    /// </summary>
    private static bool Sane(int cur, int max) =>
        max >= 10 && max <= 1_000_000 && cur >= 0 && cur <= 2_000_000;

    private static bool TryNumber(string raw, out int value)
    {
        Span<char> digits = stackalloc char[12];
        int n = 0;
        foreach (char c in raw)
        {
            if (!char.IsAsciiDigit(c)) continue;
            if (n == digits.Length) { value = 0; return false; }
            digits[n++] = c;
        }
        if (n == 0) { value = 0; return false; }
        return int.TryParse(digits[..n], out value);
    }

    private string Recognise(Bitmap bmp)
    {
        try
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;
            var decoder = BitmapDecoder.CreateAsync(ms.AsRandomAccessStream())
                                       .AsTask().GetAwaiter().GetResult();
            using var soft = decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask().GetAwaiter().GetResult();
            return _engine!.RecognizeAsync(soft).AsTask().GetAwaiter().GetResult().Text ?? "";
        }
        catch (Exception ex)
        {
            Log.Write($"ocr read failed: {ex.Message}");
            return "";
        }
    }

    /// <summary>Windows OCR ignores text this small until it is scaled up.</summary>
    private static Bitmap Upscale(Bitmap src, int scale)
    {
        var big = new Bitmap(src.Width * scale, src.Height * scale,
                             PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(big);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(src, 0, 0, big.Width, big.Height);
        return big;
    }

    private static Bitmap ToBitmap(byte[] buf, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
                                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = w * ScreenCapture.Bpp;
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(
                    buf, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    /// <summary>
    /// Finds the HUD lines by their labels and returns where each one is.
    ///
    /// The engine reports where every word it read was, so there is no need for
    /// anyone to drag boxes around text: look in the corners of the game for
    /// the word "Life", and the numbers beside it are the region.
    /// </summary>
    public Dictionary<string, Rectangle> FindLabelled(Rectangle search,
                                                      IEnumerable<string> labels)
    {
        var found = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
        if (_engine is null) return found;

        const int scale = 2;
        using var cap = new ScreenCapture();
        if (!cap.Grab(search)) return found;

        using var shot = ToBitmap(cap.Buffer, cap.Width, cap.Height);
        using var big = Upscale(shot, scale);

        OcrResult result;
        try
        {
            using var ms = new MemoryStream();
            big.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;
            var decoder = BitmapDecoder.CreateAsync(ms.AsRandomAccessStream())
                                       .AsTask().GetAwaiter().GetResult();
            using var soft = decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask().GetAwaiter().GetResult();
            result = _engine.RecognizeAsync(soft).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Write($"find numbers failed: {ex.Message}");
            return found;
        }

        foreach (var line in result.Lines)
        {
            // Only a line that carries a pair of numbers is a stat line; the
            // word alone appears in plenty of other places.
            if (!PairPattern().IsMatch(line.Text)) continue;

            foreach (string label in labels)
            {
                if (found.ContainsKey(label)) continue;
                if (!line.Text.Contains(label, StringComparison.OrdinalIgnoreCase)) continue;

                double l = double.MaxValue, t = double.MaxValue, r = 0, b = 0;
                foreach (var w in line.Words)
                {
                    l = Math.Min(l, w.BoundingRect.X);
                    t = Math.Min(t, w.BoundingRect.Y);
                    r = Math.Max(r, w.BoundingRect.X + w.BoundingRect.Width);
                    b = Math.Max(b, w.BoundingRect.Y + w.BoundingRect.Height);
                }
                if (r <= l || b <= t) continue;

                // Back to screen coordinates, with a little room around it so
                // the glyphs are not clipped on the next read.
                const int pad = 6;
                found[label] = new Rectangle(
                    search.X + (int)(l / scale) - pad,
                    search.Y + (int)(t / scale) - pad,
                    (int)((r - l) / scale) + pad * 2,
                    (int)((b - t) / scale) + pad * 2);
                Log.Write($"found \"{label}\" at {found[label]} reading \"{line.Text}\"");
            }
        }

        return found;
    }

    /// <summary>
    /// Reads a region once and parses it exactly as the watcher will, so a
    /// setup can be proved rather than assumed.
    /// </summary>
    public bool VerifyRegion(Rectangle region, string label, out int cur, out int max,
                             out string sawText)
    {
        cur = max = 0;
        sawText = ProbeOnce(region);
        return sawText.Length > 0 && TryParse(sawText, out cur, out max, label, 0);
    }

    /// <summary>
    /// One-off read, for the setup buttons. Tries a few magnifications: the
    /// engine ignores text below a certain size and gets confused above
    /// another, and where those limits fall depends on the size of the box.
    /// </summary>
    public string ProbeOnce(Rectangle region) => ProbeOnce(region, out _);

    /// <summary>As above, handing back the picture it read so an unreadable
    /// box can be looked at rather than merely reported.</summary>
    public string ProbeOnce(Rectangle region, out Bitmap? shot)
    {
        shot = null;
        if (_engine is null) return "";

        using var cap = new ScreenCapture();
        if (!cap.Grab(region)) return "";

        shot = ToBitmap(cap.Buffer, cap.Width, cap.Height);
        foreach (int scale in new[] { 3, 2, 4, 5 })
        {
            using var big = Upscale(shot, scale);
            string text = Recognise(big).Replace('\n', ' ')
                                        .Replace('\r', ' ').Trim();
            if (text.Length > 0) return text;
        }
        return "";
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(500);
        lock (_gate)
        {
            foreach (var s in _slots.Values) s.Cap.Dispose();
            _slots.Clear();
        }
        _stop.Dispose();
    }
}
