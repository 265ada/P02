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
/// not free of mistakes though - "747/747" has come back as "1747/747" - so
/// every reading is checked before it is believed, and the check that catches
/// that one is simply that current cannot exceed maximum.
/// </summary>
internal sealed partial class TextOcr : IDisposable
{
    public readonly record struct Reading(double Fraction, int Current, int Max,
                                          long AtMs, string Raw);

    [GeneratedRegex(@"([0-9][0-9.,\s]*)\s*/\s*([0-9][0-9.,\s]*)")]
    private static partial Regex PairPattern();

    private sealed class Slot
    {
        public Rectangle Region;
        public readonly ScreenCapture Cap = new();
        public Reading Last;
        public bool HasLast;
        public int StableMax;
        public int PendingMax;
        public int PendingCount;
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
    public void Configure(string name, Rectangle? region)
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

        var m = PairPattern().Match(text.Replace("\n", " "));
        if (!m.Success) return;

        if (!TryNumber(m.Groups[1].Value, out int cur)) return;
        if (!TryNumber(m.Groups[2].Value, out int max)) return;

        // The check that catches a phantom leading digit: you cannot have more
        // life than your maximum.
        if (max <= 0 || cur > max) return;

        // A maximum changes rarely, and a misread one is the difference between
        // 40% and 4%. Accept a new maximum only once it has repeated.
        if (max != slot.StableMax)
        {
            if (max == slot.PendingMax) slot.PendingCount++;
            else { slot.PendingMax = max; slot.PendingCount = 1; }

            if (slot.PendingCount < 2) return;
            slot.StableMax = max;
        }

        lock (_gate)
        {
            slot.Last = new Reading(cur / (double)max, cur, max, _clock.ElapsedMilliseconds,
                                    text.Trim());
            slot.HasLast = true;
        }
    }

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

    /// <summary>One-off read, for the setup button to show what it sees.</summary>
    public string ProbeOnce(Rectangle region)
    {
        if (_engine is null) return "";
        using var cap = new ScreenCapture();
        if (!cap.Grab(region)) return "";
        using var shot = ToBitmap(cap.Buffer, cap.Width, cap.Height);
        using var big = Upscale(shot, 3);
        return Recognise(big).Replace("\n", " ").Trim();
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
