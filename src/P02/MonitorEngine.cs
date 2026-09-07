using System.Diagnostics;

namespace P02;

public sealed record GlobeReading(string Name, double Fraction, bool Ok);

public sealed class MonitorEngine : IDisposable
{
    private readonly AppConfig _cfg;
    private CancellationTokenSource? _cts;
    private Task? _task;

    /// <summary>Master switch. Nothing is ever sent while this is false.</summary>
    public bool Armed { get; private set; }

    public event Action<GlobeReading, GlobeReading, bool>? Sampled;
    public event Action<string, double>? Fired;
    public event Action<bool>? ArmedChanged;

    public MonitorEngine(AppConfig cfg) => _cfg = cfg;

    public void SetArmed(bool value)
    {
        if (Armed == value) return;
        Armed = value;
        Log.Write(value ? "ARMED" : "DISARMED");
        ArmedChanged?.Invoke(value);
    }

    public void Toggle() => SetArmed(!Armed);

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
            while (_hist.Count > 0 && ms - _hist.Peek().Ms > 400) _hist.Dequeue();
        }

        /// <summary>How fast the globe is emptying, in percent per second.
        /// Positive means falling.</summary>
        public double DropPctPerSec(long ms, double frac)
        {
            if (_hist.Count == 0) return 0;
            var (oldMs, oldFrac) = _hist.Peek();
            long dt = ms - oldMs;
            if (dt < 60) return 0;
            return (oldFrac - frac) * 100.0 * 1000.0 / dt;
        }

        public void Reset() => _hist.Clear();
    }

    private void Loop(CancellationToken ct)
    {
        var life = new State();
        var mana = new State();
        var clock = Stopwatch.StartNew();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                long t0 = clock.ElapsedMilliseconds;
                bool focused = WindowFocused();

                var lr = Sample(life, _cfg.Life, "Life", focused, clock);
                var mr = Sample(mana, _cfg.Mana, "Mana", focused, clock);
                Sampled?.Invoke(lr, mr, focused);

                int period = 1000 / Math.Clamp(_cfg.PollHz, 1, 60);
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
            life.Cap.Dispose();
            mana.Cap.Dispose();
        }
    }

    private GlobeReading Sample(State st, WatcherConfig c, string name,
                                bool focused, Stopwatch clock)
    {
        if (!c.Region.IsValid)
            return new GlobeReading(name, 0, false);

        if (!st.Cap.Grab(c.Region.ToRect()))
            return new GlobeReading(name, 0, false);

        double frac = OrbDetector.Fraction(st.Cap.Buffer, st.Cap.Width, st.Cap.Height, c);
        long now = clock.ElapsedMilliseconds;
        double dropRate = st.DropPctPerSec(now, frac);
        st.Push(now, frac);

        // Reading always runs so the UI stays live; only firing is gated.
        if (!Armed || !c.Enabled || !focused)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true);
        }

        if (frac >= c.Threshold)
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

        try
        {
            int shots = Math.Clamp(c.BurstCount, 1, 5);
            for (int i = 0; i < shots; i++)
            {
                KeySender.Tap(c.Key, c.HoldMs);
                if (i < shots - 1) Thread.Sleep(Math.Clamp(c.BurstGapMs, 10, 500));
            }
            st.LastFireMs = clock.ElapsedMilliseconds;
            st.Below = 0;
            // The globe has not refilled yet, so old samples would read as a
            // continuing crash and inflate the drop rate.
            st.Reset();
            Log.Write($"{name}: '{c.Key}' x{shots} at {frac:P1}" +
                      (panic ? $" PANIC (drop {dropRate:0}%/s)" : ""));
            Fired?.Invoke(name, frac);
        }
        catch (Exception ex)
        {
            Log.Write($"{name}: send failed: {ex.Message}");
        }

        return new GlobeReading(name, frac, true);
    }

    private bool WindowFocused()
    {
        string match = _cfg.WindowMatch?.Trim() ?? string.Empty;
        if (match.Length == 0) return true;
        return Native.ForegroundTitle()
                     .Contains(match, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Stop();
}
