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

    private readonly SoundPlayer _player;
    private readonly MemoryStream _wav;
    private long _lastMs;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public Chime()
    {
        _wav = new MemoryStream(BuildWav(frequency: 1180, milliseconds: 55, amplitude: 0.18));
        _player = new SoundPlayer(_wav);
        try { _player.Load(); } catch { /* no audio device; play becomes a no-op */ }
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
        try { _player.Play(); } catch { /* never let a sound break the loop */ }
    }

    /// <summary>
    /// A 16-bit mono WAV holding one tone. The envelope matters more than the
    /// pitch: a fast attack and a smooth decay reads as a soft "ding" rather
    /// than a click or a beep.
    /// </summary>
    private static byte[] BuildWav(double frequency, int milliseconds, double amplitude)
    {
        int samples = SampleRate * milliseconds / 1000;
        var pcm = new byte[samples * 2];

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

            short v = (short)(wave / 1.25 * env * amplitude * short.MaxValue);
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
        _player.Dispose();
        _wav.Dispose();
    }
}
