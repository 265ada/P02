using System.Diagnostics;

namespace P02;

/// <summary>One globe's latest sample. <paramref name="Ok"/> false means there
/// is no reading, and Note says why.</summary>
public sealed record GlobeReading(string Name, double Fraction, bool Ok, string Note = "");

public sealed class MonitorEngine : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly KeyPresser _keys = new();
    private readonly Chime _chime;
    private CancellationTokenSource? _cts;
    private Task? _task;

    /// <summary>Master switch. Nothing is ever sent while this is false.</summary>
    public bool Armed { get; private set; }

    /// <summary>Polls actually completed in the last second.</summary>
    public int ActualHz { get; private set; }

    /// <summary>Milliseconds of work in the last poll, excluding the sleep.</summary>
    public double LastPollMs { get; private set; }

    /// <summary>Title of whatever window currently has focus, for the UI.</summary>
    public string ForegroundTitle { get; private set; } = "";

    public event Action<GlobeReading, GlobeReading, bool>? Sampled;
    public event Action<string, double>? Fired;

    /// <summary>Globe name, whether the last press moved the globe, and how
    /// many in a row have not.</summary>
    public event Action<string, bool, int>? EffectChecked;
    public event Action<bool>? ArmedChanged;

    public MonitorEngine(AppConfig cfg)
    {
        _cfg = cfg;
        _chime = new Chime(cfg.SoundGainDb);
    }

    /// <summary>Loudest boost the ding can take without clipping, in dB.</summary>
    public static int MaxGainDb => Chime.MaxGainDb;

    /// <summary>Rebuilds the ding at a new level and plays it once.</summary>
    public void SetSoundGain(int db)
    {
        _chime.GainDb = db;
        _chime.Play(0);
    }

    public void SetArmed(bool value)
    {
        if (Armed == value) return;
        Armed = value;
        Log.Write(value ? "ARMED" : "DISARMED");
        ArmedChanged?.Invoke(value);
    }

    public void Toggle() => SetArmed(!Armed);

    /// <summary>Fires a key straight away, ignoring arm state, so a keybind can
    /// be proven to reach the game.</summary>
    public void TestKey(string key, int holdMs) => _keys.Send(key, holdMs);

    public void Start()
    {
        if (_task is not null) return;
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _task?.Wait(1000); } catch { /* shutting down */ }
        _task = null;
    }

    private sealed class State
    {
        public readonly ScreenCapture Cap = new();
        public int Below;
        public long LastFireMs = long.MinValue / 2;

        // Short history, so we can tell a slow bleed from a hit that is about
        // to kill us and react differently to each.
        private readonly Queue<(long Ms, double Frac)> _hist = new();

        public void Push(long ms, double frac)
        {
            _hist.Enqueue((ms, frac));
            while (_hist.Count > 0 && ms - _hist.Peek().Ms > 350) _hist.Dequeue();
        }

        /// <summary>How fast the globe is emptying, in percent per second.
        /// Positive means falling.</summary>
        public double DropPctPerSec(long ms, double frac)
        {
            if (_hist.Count == 0) return 0;
            var (oldMs, oldFrac) = _hist.Peek();
            long dt = ms - oldMs;
            if (dt < 40) return 0;
            return (oldFrac - frac) * 100.0 * 1000.0 / dt;
        }

        public void Reset() => _hist.Clear();

        // --- effect verification ---
        public bool Verifying;
        public long FireMs;
        public double FracAtFire;
        public double MaxSinceFire;
        public int NoEffect;
        public long RecoveringUntilMs;
    }

    private void Loop(CancellationToken ct)
    {
        var life = new State();
        var mana = new State();
        var clock = Stopwatch.StartNew();

        long lastUiMs = 0, hzWindowMs = 0, lastLogMs = 0;
        int polls = 0;

        // Without this the scheduler rounds every sleep up to ~15 ms, which
        // caps the loop near 60 Hz no matter what poll rate is asked for.
        Native.timeBeginPeriod(1);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long t0 = clock.ElapsedMilliseconds;
                bool focused = WindowFocused();

                var lr = Sample(life, _cfg.Life, "Life", focused, clock);
                var mr = Sample(mana, _cfg.Mana, "Mana", focused, clock);

                LastPollMs = clock.Elapsed.TotalMilliseconds - t0;

                polls++;
                if (t0 - hzWindowMs >= 1000)
                {
                    ActualHz = polls;
                    polls = 0;
                    hzWindowMs = t0;
                }

                // A readable trail of what was seen while armed, so a session
                // that failed to fire can be explained afterwards rather than
                // guessed at.
                if (Armed && t0 - lastLogMs >= 2000)
                {
                    lastLogMs = t0;
                    Log.Write($"watch  life {lr.Fraction:P1}{(_cfg.Life.Enabled ? "" : " (off)")}" +
                              $"  mana {mr.Fraction:P1}{(_cfg.Mana.Enabled ? "" : " (off)")}" +
                              $"  focused={focused}  hz={ActualHz}  " +
                              $"skipped={_keys.Skipped}  poll={LastPollMs:0.0}ms");
                }

                // The UI cannot use 100 samples a second and repainting that
                // often would slow the loop it is reporting on.
                if (t0 - lastUiMs >= 60)
                {
                    lastUiMs = t0;
                    Sampled?.Invoke(lr, mr, focused);
                }

                int period = 1000 / Math.Clamp(_cfg.PollHz, 5, 250);
                int sleep = period - (int)(clock.ElapsedMilliseconds - t0);
                if (sleep > 0) Thread.Sleep(sleep);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"monitor loop died: {ex}");
        }
        finally
        {
            Native.timeEndPeriod(1);
            life.Cap.Dispose();
            mana.Cap.Dispose();
        }
    }

    private GlobeReading Sample(State st, WatcherConfig c, string name,
                                bool focused, Stopwatch clock)
    {
        // Switched off means not looked at. Capturing costs about 9 ms whatever
        // the region size, so reading a globe nobody asked about was spending
        // half the loop's budget to update a number that changes nothing.
        if (!c.Enabled)
        {
            st.Below = 0;
            st.Reset();
            return new GlobeReading(name, 0, false, "off");
        }

        if (!c.Region.IsValid)
            return new GlobeReading(name, 0, false, "no region");

        if (!st.Cap.Grab(c.Region.ToRect()))
            return new GlobeReading(name, 0, false, "capture failed");

        double frac = OrbDetector.Fraction(st.Cap.Buffer, st.Cap.Width, st.Cap.Height, c);
        long now = clock.ElapsedMilliseconds;
        double dropRate = st.DropPctPerSec(now, frac);
        st.Push(now, frac);

        // A press that worked shows up as the globe climbing. Anything else is
        // a press that went nowhere, and that is worth saying out loud.
        if (st.Verifying)
        {
            st.MaxSinceFire = Math.Max(st.MaxSinceFire, frac);
            if (st.MaxSinceFire > st.FracAtFire + 0.015)
            {
                st.Verifying = false;
                st.NoEffect = 0;
                st.RecoveringUntilMs = now + 600;
                EffectChecked?.Invoke(name, true, 0);
            }
            else if (now - st.FireMs > c.VerifyWindowMs)
            {
                st.Verifying = false;
                st.NoEffect++;
                Log.Write($"{name}: press had no effect ({st.NoEffect} in a row) - "
                          + "no charges, wrong key, or input not reaching the game");
                EffectChecked?.Invoke(name, false, st.NoEffect);
            }
        }

        // Reading runs whenever the globe is on, so the UI stays live even
        // while disarmed; only firing is gated below.
        if (!Armed || !focused)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true);
        }

        if (frac >= c.Threshold)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true);
        }

        // A globe that reads flat zero is usually one we cannot see at all —
        // dead, loading, or covered. The logs from a real session showed over a
        // thousand presses fired into exactly this state.
        if (frac <= c.IgnoreBelow)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true);
        }

        // Deep in the red, or dropping fast enough that waiting a full cooldown
        // means dying with charges unspent: press again as soon as the game
        // will accept it, and do not wait for a second confirming frame.
        bool panic = frac < c.PanicBelow || dropRate >= c.FastDropPctPerSec;
        int gap = panic ? c.PanicCooldownMs : c.CooldownMs;
        int confirm = panic ? 1 : Math.Max(1, c.ConfirmFrames);

        st.Below++;
        if (st.Below < confirm || now - st.LastFireMs < gap)
            return new GlobeReading(name, frac, true);

        // A burst still going out means the previous request has not even
        // finished leaving; asking for another only builds a backlog.
        if (_keys.Busy)
            return new GlobeReading(name, frac, true);

        // Recovery is already running and working. Stacking another flask on
        // top spends a charge for recovery that will be cut off the moment the
        // globe fills, so this is optional and off by default.
        if (c.SkipWhileRecovering && now < st.RecoveringUntilMs)
            return new GlobeReading(name, frac, true);

        int shots = Math.Clamp(c.BurstCount, 1, 5);
        if (!_keys.Send(c.Key, c.HoldMs, shots, Math.Clamp(c.BurstGapMs, 5, 500)))
            return new GlobeReading(name, frac, true);

        st.LastFireMs = now;
        st.Below = 0;

        if (c.VerifyEffect)
        {
            st.Verifying = true;
            st.FireMs = now;
            st.FracAtFire = frac;
            st.MaxSinceFire = frac;
        }
        // The globe has not refilled yet, so old samples would read as a
        // continuing crash and inflate the drop rate.
        st.Reset();
        if (_cfg.SoundOnFire) _chime.Play(_cfg.SoundGapMs);

        Log.Write($"{name}: '{c.Key}' x{shots} at {frac:P1}" +
                  (panic ? $" PANIC (drop {dropRate:0}%/s)" : ""));
        Fired?.Invoke(name, frac);

        return new GlobeReading(name, frac, true);
    }

    private bool WindowFocused()
    {
        string title = Native.ForegroundTitle();
        ForegroundTitle = title;

        string match = _cfg.WindowMatch?.Trim() ?? string.Empty;
        if (match.Length == 0) return true;
        return title.Contains(match, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Plays the ding once, ignoring the gap, so it can be auditioned.</summary>
    public void TestSound() => _chime.Play(0);

    public void Dispose()
    {
        Stop();
        _keys.Dispose();
        _chime.Dispose();
    }
}
