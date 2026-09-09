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
        public long PendingSinceMs;
        public long LastComplaintMs = long.MinValue / 2;
        public int DisagreeMax;
        public int DisagreeCount;
        public int Suggested;

        /// <summary>Which preparation last produced a pair; tried first next time.</summary>
        public int Cut;
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
    /// <summary>
    /// The maximum currently being read and settled on, or 0.
    ///
    /// Only while it is still being read. The settled value used to be handed
    /// out long after the numbers had stopped coming, so a maximum from an
    /// earlier character or an earlier set of gear was adopted over and over -
    /// "Maximum changed from 1,138 to 1,190 - updated to match", on a character
    /// whose life is 1,138, from a reading minutes old.
    /// </summary>
    public int StableMaxOf(string name)
    {
        lock (_gate)
        {
            if (!_slots.TryGetValue(name, out var slot) || !slot.HasLast) return 0;
            return _clock.ElapsedMilliseconds - slot.Last.AtMs < 5000 ? slot.StableMax : 0;
        }
    }

    /// <summary>
    /// A maximum that keeps being read while a different one is configured, or
    /// 0 when there is no such disagreement.
    /// </summary>
    public int SuggestedMax(string name)
    {
        lock (_gate)
        {
            if (!_slots.TryGetValue(name, out var slot) || !slot.HasLast) return 0;
            return _clock.ElapsedMilliseconds - slot.Last.AtMs < 5000 ? slot.Suggested : 0;
        }
    }

    /// <summary>
    /// The preparations to try, best-known first.
    /// </summary>
    private static IEnumerable<int> Cuts(int known)
    {
        yield return known;
        foreach (int cut in new[] { 0, 170, 200, 140 })
            if (cut != known) yield return cut;
    }

    private int _intervalMs = 160;

    /// <summary>How often to read, in milliseconds.</summary>
    public void SetInterval(int ms) => Volatile.Write(ref _intervalMs, Math.Clamp(ms, 40, 1000));

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

            // Reading rate follows how close to trouble you are. Six times a
            // second is plenty while healthy and far too slow while dropping:
            // at that rate a reading can be a fifth of a second old before it
            // is even looked at.
            _stop.Token.WaitHandle.WaitOne(Volatile.Read(ref _intervalMs));
        }
    }

    private void ReadOne(string name, Slot slot)
    {
        Rectangle r;
        lock (_gate) r = slot.Region;
        if (r.Width < 8 || r.Height < 4) return;
        if (!slot.Cap.Grab(r)) return;

        // The running read gets the same treatment as the setup one, or a line
        // over bright ground reads once when it is set up and never again.
        // Which preparation worked last time is remembered and tried first, so
        // the common case is still a single recognise.
        string text = "";
        Interlocked.Increment(ref _readersWaiting);
        try
        {
            using var shot = ToBitmap(slot.Cap.Buffer, slot.Cap.Width, slot.Cap.Height);
            foreach (int cut in Cuts(slot.Cut))
            {
                using var prepared = cut == 0 ? null : Threshold(shot, cut);
                using var big = Upscale(prepared ?? shot, 3);
                text = Recognise(big);

                if (PairPattern().IsMatch(text)) { slot.Cut = cut; break; }
            }
        }
        finally { Interlocked.Decrement(ref _readersWaiting); }

        if (text.Length == 0) return;

        string label;
        int expected;
        lock (_gate) { label = slot.Label; expected = slot.ExpectedMax; }

        if (!TryParse(text, out int cur, out int max, label, expected))
        {
            // Silence here is correct - a refused reading is better than the
            // wrong line - but it should be explicable.
            if (label.Length > 0
                && !HasLabel(text, label)
                && _clock.ElapsedMilliseconds - slot.LastComplaintMs > 10000)
            {
                slot.LastComplaintMs = _clock.ElapsedMilliseconds;
                string seen = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
                Log.Write($"{name}: read \"{seen}\" but it does not contain "
                          + $"\"{label}\" - include that word in the box");
            }
            return;
        }

        // A stated maximum used to reject anything that disagreed with it. That
        // is exactly backwards for somebody levelling: the maximum changes, and
        // from that moment every reading is refused - which reads as "numbers
        // not on screen" on a screen with the numbers plainly on it, and means
        // holding fire until somebody notices and retypes it.
        //
        // The maximum is a thing to be read, not a thing to be checked against.
        // What the check was guarding - a stray digit turning 1,465 into 11,465
        // - is caught by making a new maximum prove itself over time below, and
        // by refusing a current more than twice its maximum. Both of those work
        // without anyone typing anything.
        //
        // The disagreement is still counted, so the panel can say the stored
        // value has moved on.
        if (expected > 0 && max != expected)
        {
            if (max == slot.DisagreeMax) slot.DisagreeCount++;
            else { slot.DisagreeMax = max; slot.DisagreeCount = 1; }

            if (slot.DisagreeCount >= 3)
            {
                lock (_gate) slot.Suggested = max;
                if (_clock.ElapsedMilliseconds - slot.LastComplaintMs > 30000)
                {
                    slot.LastComplaintMs = _clock.ElapsedMilliseconds;
                    Log.Write($"{name}: maximum reads {max} where {expected} is stored - "
                              + "taking the reading");
                }
            }
        }
        else
        {
            slot.DisagreeCount = 0;
            lock (_gate) slot.Suggested = 0;
        }

        // Without a stated maximum, lean on the fact that a real one barely
        // ever changes. A small change might be a gear swap; a large one is
        // almost always a digit that was not there, so make it prove itself.
        if (max != slot.StableMax)
        {
            bool first = slot.StableMax == 0;
            bool big = slot.StableMax > 0
                       && Math.Abs(max - slot.StableMax) > slot.StableMax / 5;

            long nowMs = _clock.ElapsedMilliseconds;
            if (max == slot.PendingMax) slot.PendingCount++;
            else { slot.PendingMax = max; slot.PendingCount = 1; slot.PendingSinceMs = nowMs; }

            // The first maximum was the cheapest to accept and is by far the
            // most expensive to get wrong: everything downstream is built on
            // it, including the memory search, which then locks onto whatever
            // address happens to hold that number and reads its neighbour as
            // your life. Two readings was not proof of anything - OCR misreads
            // the same pixels the same way every time, so a misread repeats as
            // readily as the truth. Only time separates them: 1,490 stays 1,490
            // across seconds of frames, while a misread comes and goes.
            int need = first ? 8 : big ? 5 : 2;
            int span = first || big ? 2000 : 0;

            if (slot.PendingCount < need || nowMs - slot.PendingSinceMs < span) return;

            Log.Write(first
                ? $"{name}: maximum settled on {max} after {slot.PendingCount} readings "
                  + $"over {(nowMs - slot.PendingSinceMs) / 1000.0:0.0}s"
                : big ? $"{name}: maximum changed from {slot.StableMax} to {max}"
                : $"{name}: maximum {slot.StableMax} -> {max}");
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
    /// Whether a line carries this label, allowing for the engine getting a
    /// letter of it wrong.
    ///
    /// Demanding the exact word threw away whole frames - numbers included -
    /// over "tife", "Li" and "Li fiv", which is small pale text on a moving
    /// background read three times a second. The label is there to say which
    /// line this is, and a word that is one letter out still says it: nothing
    /// else in the box looks remotely like "Life". The numbers themselves are
    /// never guessed at, only the word beside them.
    /// </summary>
    internal static bool HasLabel(string text, string label)
    {
        if (label.Length == 0) return true;
        if (text.Contains(label, StringComparison.OrdinalIgnoreCase)) return true;

        char[] gaps = [' ', (char)10, (char)13, (char)9, ','];
        foreach (string word in text.Split(gaps, StringSplitOptions.RemoveEmptyEntries))
        {
            // A truncated read - "Li" for "Life" - is still unambiguous when
            // nothing else in the box starts that way.
            if (word.Length >= 2 && label.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                return true;

            if (word.Length == label.Length && Off(word, label) <= 1) return true;
        }

        return false;

        static int Off(string a, string b)
        {
            int wrong = 0;
            for (int i = 0; i < a.Length; i++)
                if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i])) wrong++;
            return wrong;
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
            l => label.Length > 0 && HasLabel(l, label),
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
    /// <summary>
    /// Whether a parsed pair could be a real pool.
    ///
    /// Overhealing past the maximum is ordinary - 1,947 out of 1,490 happens on
    /// any character with the skills for it - so a current above its maximum is
    /// accepted. Ten times it is not: "14,610/1,490" appeared mid-fight, which
    /// is 1,461 with a digit that was not there, and it clamps to a comfortable
    /// 100% no matter how little life is actually left. The maximum being right
    /// does not vouch for the current beside it.
    /// </summary>
    private static bool Sane(int cur, int max) =>
        max >= 10 && max <= 1_000_000 && cur >= 0 && cur <= max * 2;

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

    /// <summary>
    /// One recogniser, one caller at a time.
    ///
    /// The engine refuses a second call while one is in flight - "Another
    /// RecognizeAsync operation is already running!" - and every one of those
    /// is a reading lost. It was rare while each read was a single recognise;
    /// sweeping several preparations made the background reader and the setup
    /// search collide constantly, and the numbers went dark again.
    /// </summary>
    private static readonly object Recogniser = new();

    /// <summary>
    /// How many live readings are waiting for the recogniser.
    ///
    /// The search that looks for the stat lines works on a whole corner of the
    /// screen, and a corner blown up four times is a picture of several million
    /// pixels - one such pass can take a second or more on its own. The reading
    /// that decides whether to press a key was queueing behind those, so on a
    /// machine where the numbers keep needing to be found again, life updated
    /// once every second or three. This lets the search stand aside.
    /// </summary>
    private static int _readersWaiting;

    private string Recognise(Bitmap bmp)
    {
        lock (Recogniser)
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
    /// <summary>
    /// Black text on white, from bright text on whatever the world is.
    ///
    /// This is the difference between the two corners. Mana is drawn over dark
    /// water and reads first time; Life over bright cobblestone comes back as
    /// the word alone, because the pale digits are barely lighter than the
    /// ground behind them. The engine wants document contrast and the game
    /// gives it none.
    ///
    /// The HUD text is close to white, so everything near white becomes text
    /// and everything else becomes page. Nothing is guessed at - it either
    /// separates cleanly or the raw image is used instead.
    /// </summary>
    private static Bitmap Threshold(Bitmap src, int cut)
    {
        var flat = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);

        var box = new Rectangle(0, 0, src.Width, src.Height);
        var from = src.LockBits(box, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var to = flat.LockBits(box, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int count = src.Width * src.Height;
            var line = new byte[count * 4];
            System.Runtime.InteropServices.Marshal.Copy(from.Scan0, line, 0, line.Length);

            for (int i = 0; i < line.Length; i += 4)
            {
                int lum = (line[i + 2] * 30 + line[i + 1] * 59 + line[i] * 11) / 100;
                byte v = lum >= cut ? (byte)0 : (byte)255;
                line[i] = line[i + 1] = line[i + 2] = v;
                line[i + 3] = 255;
            }

            System.Runtime.InteropServices.Marshal.Copy(line, 0, to.Scan0, line.Length);
        }
        finally
        {
            src.UnlockBits(from);
            flat.UnlockBits(to);
        }

        return flat;
    }

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
    /// <summary>What one recognise pass saw, in screen coordinates.</summary>
    private readonly record struct Seen(string Text, Rectangle Where, bool IsWord);

    /// <summary>
    /// Locates stat lines by their labels.
    ///
    /// The label and its numbers do not arrive together. Over a game the raw
    /// crop gives up the word and loses the pale digits, while the thresholded
    /// one gives up the digits and loses the word - and even within one pass
    /// they come back as separate lines, because the HUD leaves a wide gap
    /// between them. Insisting on both in one line matched nothing at all:
    ///
    ///   raw         -> [Shield]
    ///   thresholded -> [2,065/2,065]
    ///
    /// So every pass contributes what it saw and the answer is assembled from
    /// all of it by position: a word that names a stat, and the nearest pair of
    /// numbers sharing its line. Which pass each came from stops mattering.
    /// </summary>
    public Dictionary<string, Rectangle> FindLabelled(Rectangle search,
                                                      IEnumerable<string> labels)
    {
        var want = labels.ToArray();
        var seen = new List<Seen>();

        foreach (int cut in new[] { 0, 170, 200 })
            foreach (int scale in new[] { 2, 3, 4 })
                Collect(search, scale, cut, seen);

        var found = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
        var pairs = seen.Where(o => !o.IsWord && PairPattern().IsMatch(o.Text)).ToList();

        foreach (string label in want)
        {
            var word = seen.Where(o => o.IsWord && HasLabel(o.Text, label))
                           .OrderBy(o => o.Where.Y)
                           .Select(o => (Seen?)o)
                           .FirstOrDefault();
            if (word is null) continue;

            var w = word.Value.Where;
            int mid = w.Y + w.Height / 2;

            // The pair on the same line. Same line means its vertical middle
            // sits inside the word's own height - one line down is a different
            // stat, and reading shield as life is the dangerous direction.
            var pair = pairs
                .Where(o => Math.Abs(o.Where.Y + o.Where.Height / 2 - mid) <= w.Height / 2
                            && o.Where.Right > w.Right)
                .OrderBy(o => Math.Abs(o.Where.Y + o.Where.Height / 2 - mid))
                .Select(o => (Seen?)o)
                .FirstOrDefault();
            if (pair is null) continue;

            var box = Rectangle.Union(w, pair.Value.Where);
            // Generous. A box cut close to its own glyphs reads back as the
            // word and none of the numbers: the engine wants margin around what
            // it is asked to read, and a HUD line has none of its own.
            box.Inflate(26, 14);
            box.Intersect(search);
            found[label] = box;
            Log.Write($"found {label} at {box} - word \"{word.Value.Text}\" with "
                      + $"\"{pair.Value.Text}\"");
        }

        Infer(search, found);
        return found;
    }

    /// <summary>
    /// The stack of "current/maximum" pairs in a corner, top to bottom.
    ///
    /// A last resort that needs no labels at all. Life, shield and ward are the
    /// only things in that corner written as a pair of numbers, stacked, all
    /// starting at the same left edge - chat and item names are not. So when
    /// none of the words can be read, the block can still be found by its
    /// shape, and the top line of it is life.
    /// </summary>
    public List<Rectangle> FindStackedPairs(Rectangle search)
    {
        var seen = new List<Seen>();
        foreach (int cut in new[] { 0, 170, 200 })
            foreach (int scale in new[] { 2, 3 })
                Collect(search, scale, cut, seen);

        // A pair of numbers is not enough on its own - a flask counter reads
        // "1/1" and sits in the same corner. A pool has a maximum worth having.
        var pairs = new List<Seen>();
        foreach (var o in seen)
        {
            if (o.IsWord || !PairPattern().IsMatch(o.Text)) continue;
            if (!TryParse(o.Text, out _, out int max) || max < 50) continue;
            pairs.Add(o);
        }

        // The same line read by several preparations comes back several times.
        var rows = new List<Rectangle>();
        foreach (var p in pairs.OrderBy(o => o.Where.Y))
        {
            int i = rows.FindIndex(r => Math.Abs(r.Y - p.Where.Y) <= p.Where.Height);
            if (i < 0) rows.Add(p.Where);
            else rows[i] = Rectangle.Union(rows[i], p.Where);
        }

        // Only rows that line up with each other: a stack shares a left edge.
        if (rows.Count > 1)
        {
            int edge = rows.Min(r => r.X);
            rows = rows.Where(r => r.X - edge <= 40).ToList();
        }

        return rows.OrderBy(r => r.Y).ToList();
    }

    /// <summary>Adds everything one preparation could read to the pile.</summary>
    private void Collect(Rectangle search, int scale, int cut, List<Seen> into)
    {
        if (_engine is null) return;

        // A crop this size at this magnification is minutes of work for
        // nothing: the engine has an upper limit and the cost is quadratic. The
        // small magnifications find these lines anyway.
        if ((long)search.Width * scale > 3000 || (long)search.Height * scale > 3000) return;

        // Let anything reading a live pool go first.
        for (int waited = 0; Volatile.Read(ref _readersWaiting) > 0 && waited < 40; waited++)
            Thread.Sleep(5);

        using var cap = new ScreenCapture();
        if (!cap.Grab(search)) return;

        using var shot = ToBitmap(cap.Buffer, cap.Width, cap.Height);
        using var prepared = cut == 0 ? null : Threshold(shot, cut);
        using var big = Upscale(prepared ?? shot, scale);

        OcrResult result;
        lock (Recogniser)
        {
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
                return;
            }
        }

        foreach (var line in result.Lines)
        {
            Rectangle? bounds = null;
            foreach (var word in line.Words)
            {
                var at = Screen(word.BoundingRect, search, scale);
                into.Add(new Seen(word.Text, at, true));
                bounds = bounds is null ? at : Rectangle.Union(bounds.Value, at);
            }

            if (bounds is not null) into.Add(new Seen(line.Text, bounds.Value, false));
        }
    }

    private static Rectangle Screen(Windows.Foundation.Rect r, Rectangle search, int scale) =>
        new(search.X + (int)(r.X / scale), search.Y + (int)(r.Y / scale),
            Math.Max(1, (int)(r.Width / scale)), Math.Max(1, (int)(r.Height / scale)));

    /// <summary>
    /// Fills in a line that was not read from one that was.
    ///
    /// Life, Shield and Ward are one stacked block: same corner, same width,
    /// evenly spaced. Finding any of them locates the others whether or not
    /// their own word came back readable - and Life is the one that keeps not
    /// coming back, sitting at the top of the block where the capture edge cuts
    /// closest to the glyphs.
    /// </summary>
    private static void Infer(Rectangle search, Dictionary<string, Rectangle> found)
    {
        if (!found.ContainsKey("Life") && found.TryGetValue("Shield", out var shield))
        {
            var life = new Rectangle(shield.X, shield.Y - shield.Height,
                                     shield.Width, shield.Height);
            if (life.Y >= search.Y)
            {
                found["Life"] = life;
                Log.Write($"placed Life at {life} - one line above Shield, which was "
                          + "found; its own word did not come back readable");
            }
        }
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

        // Raw first - right often enough, and it costs nothing - then the same
        // crop thresholded, for digits on ground bright enough to swallow them.
        // A read that comes back with the word and no numbers is not a success;
        // the numbers are the point.
        foreach (int cut in new[] { 0, 170, 200, 140 })
        {
            using var prepared = cut == 0 ? null : Threshold(shot, cut);
            foreach (int scale in new[] { 3, 2, 4 })
            {
                using var big = Upscale(prepared ?? shot, scale);
                string text = Flatten(Recognise(big));
                if (PairPattern().IsMatch(text)) return text;
            }
        }

        // A tight crop is the usual reason for failing. The engine reads a line
        // far better with space around it than pressed against its edges, and a
        // HUD line has no margin of its own.
        using (var roomy = new ScreenCapture())
        {
            if (roomy.Grab(Rectangle.Inflate(region, 44, 18)))
            {
                using var shot2 = ToBitmap(roomy.Buffer, roomy.Width, roomy.Height);
                foreach (int cut in new[] { 170, 0, 200 })
                {
                    using var prepared = cut == 0 ? null : Threshold(shot2, cut);
                    using var big = Upscale(prepared ?? shot2, 3);
                    string text = Flatten(Recognise(big));
                    if (PairPattern().IsMatch(text)) return text;
                }
            }
        }

        // Nothing carried a pair. Hand back whatever was legible, so the panel
        // can say what it saw rather than only that it saw nothing.
        foreach (int scale in new[] { 3, 2 })
        {
            using var big = Upscale(shot, scale);
            string text = Flatten(Recognise(big));
            if (text.Length > 0) return text;
        }

        return "";
    }

    private static string Flatten(string raw) =>
        raw.Replace((char)10, ' ').Replace((char)13, ' ').Trim();

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
