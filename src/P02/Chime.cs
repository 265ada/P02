using System.Media;

namespace P02;

/// <summary>
/// A short, quiet ding for when a key goes out.
///
/// Console.Beep blocks the calling thread and sounds harsh, and the system
/// sounds are all too long or too loud for something that can happen twice a
/// second. This synthesises a soft tone once at startup and replays it.
/// </summary>
internal sealed class Chime : IDisposable
{
    private const int SampleRate = 44100;

    /// <summary>
    /// Peak of the original sound as a fraction of full scale. This is the 0 dB
    /// reference, so existing settings sound exactly as they did.
    /// </summary>
    private const double BasePeak = 0.1532;

    /// <summary>
    /// Never synthesise above this. Full scale is where clipping starts, and a
    /// clipped sine buzzes; a little under it stays clean.
    /// </summary>
    private const double CleanCeiling = 0.97;

    /// <summary>Boost available from level alone, before the ceiling is hit.</summary>
    public static int CleanGainDb { get; } =
        (int)Math.Floor(20 * Math.Log10(CleanCeiling / BasePeak));

    /// <summary>
    /// Loudest setting offered. Past <see cref="CleanGainDb"/> the peak cannot
    /// go any higher - full scale is full scale - so extra loudness comes from
    /// filling in the gap between the peak and the average instead. A ding is a
    /// sharp spike with a fast decay, so most of its length sits well below its
    /// own peak, and rounding that off with a soft curve is a large gain in how
    /// loud it sounds for a small, short-lived change in tone.
    /// </summary>
    public static int MaxGainDb => CleanGainDb + 10;

    private SoundPlayer _player;
    private MemoryStream _wav;
    private readonly object _swap = new();
    private int _gainDb;
    private long _lastMs;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // SoundPlayer.Play goes through PlaySound, which can stall for a long time
    // when something else holds the audio device - a game using exclusive-mode
    // output, for instance. On the monitor thread that stall is time spent not
    // watching your health, so playing happens somewhere else entirely.
    private readonly SemaphoreSlim _pending = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    public Chime(int gainDb = 0)
    {
        _gainDb = Math.Clamp(gainDb, -24, MaxGainDb);
        (_wav, _player) = Build(_gainDb);

        _thread = new Thread(Run) { IsBackground = true, Name = "P02 chime" };
        _thread.Start();
    }

    /// <summary>
    /// Boost over the original level, in dB. 0 is what it always was; the
    /// maximum is whatever fits under the clipping ceiling.
    /// </summary>
    public int GainDb
    {
        get => _gainDb;
        set
        {
            int g = Math.Clamp(value, -24, MaxGainDb);
            if (g == _gainDb) return;
            lock (_swap)
            {
                _gainDb = g;
                var old = (_wav, _player);
                (_wav, _player) = Build(g);
                old._player.Dispose();
                old._wav.Dispose();
            }
        }
    }

    private static (MemoryStream, SoundPlayer) Build(int gainDb)
    {
        double wanted = BasePeak * Math.Pow(10, gainDb / 20.0);
        double peak = Math.Min(wanted, CleanCeiling);

        // Whatever was asked for beyond the ceiling becomes drive instead.
        double drive = wanted > CleanCeiling ? wanted / CleanCeiling : 1.0;

        var wav = new MemoryStream(
            BuildWav(frequency: 1180, milliseconds: 55, peak: peak, drive: drive));
        var player = new SoundPlayer(wav);
        try { player.Load(); } catch { /* no audio device; play becomes a no-op */ }
        return (wav, player);
    }

    private void Run()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                _pending.Wait(_stop.Token);
                try
                {
                    lock (_swap) _player.Play();
                }
                catch { /* never let a sound matter */ }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (ObjectDisposedException) { /* shutting down */ }
    }

    /// <summary>
    /// Plays, unless one sounded less than <paramref name="minGapMs"/> ago.
    /// Firing can repeat several times a second, and a ding per press would be
    /// a machine gun.
    /// </summary>
    public void Play(int minGapMs)
    {
        long now = _clock.ElapsedMilliseconds;
        if (now - _lastMs < minGapMs) return;
        _lastMs = now;

        // Signal and return; the sound thread does the waiting.
        if (_pending.CurrentCount == 0) _pending.Release();
    }

    /// <summary>
    /// A 16-bit mono WAV holding one tone. The envelope matters more than the
    /// pitch: a fast attack and a smooth decay reads as a soft "ding" rather
    /// than a click or a beep.
    /// </summary>
    /// <summary>
    /// <paramref name="peak"/> is the loudest sample as a fraction of full
    /// scale. The waveform is built first and then scaled to exactly that, so
    /// the level is precise and clipping is impossible by construction rather
    /// than by choosing a cautious multiplier.
    /// </summary>
    private static byte[] BuildWav(double frequency, int milliseconds,
                                   double peak, double drive = 1.0)
    {
        int samples = SampleRate * milliseconds / 1000;
        var raw = new double[samples];
        double loudest = 0;

        int attack = Math.Max(1, samples / 12);
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)SampleRate;

            // Rise quickly, then decay exponentially to silence.
            double env = i < attack
                ? i / (double)attack
                : Math.Exp(-5.0 * (i - attack) / (samples - attack));

            // A touch of the octave above gives it a bell edge instead of a
            // flat sine tone.
            double wave = Math.Sin(2 * Math.PI * frequency * t)
                        + 0.25 * Math.Sin(4 * Math.PI * frequency * t);

            raw[i] = wave * env;
            loudest = Math.Max(loudest, Math.Abs(raw[i]));
        }

        // Normalise, shape, then set the level. Soft saturation lifts the
        // quiet tail towards the peak without ever exceeding it, so this adds
        // loudness rather than clipping.
        if (drive > 1.0 && loudest > 0)
        {
            double k = Math.Clamp((drive - 1.0) * 2.0 + 0.75, 0.75, 5.0);
            double norm = Math.Tanh(k);
            for (int i = 0; i < samples; i++)
                raw[i] = Math.Tanh(k * raw[i] / loudest) / norm;
            loudest = 1.0;
        }

        var pcm = new byte[samples * 2];
        double scale = loudest > 0 ? peak * short.MaxValue / loudest : 0;
        for (int i = 0; i < samples; i++)
        {
            short v = (short)Math.Round(raw[i] * scale);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                        // PCM header size
        w.Write((short)1);                  // PCM
        w.Write((short)1);                  // mono
        w.Write(SampleRate);
        w.Write(SampleRate * 2);            // byte rate
        w.Write((short)2);                  // block align
        w.Write((short)16);                 // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread.Join(300);
        _stop.Dispose();
        _pending.Dispose();
        lock (_swap)
        {
            _player.Dispose();
            _wav.Dispose();
        }
    }
}
