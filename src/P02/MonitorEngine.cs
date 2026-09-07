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

        // Reading always runs so the UI stays live; only firing is gated.
        if (!Armed || !c.Enabled || !focused)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true);
        }

        if (frac < c.Threshold)
        {
            st.Below++;
            long now = clock.ElapsedMilliseconds;
            if (st.Below >= Math.Max(1, c.ConfirmFrames) &&
                now - st.LastFireMs >= c.CooldownMs)
            {
                try
                {
                    KeySender.Tap(c.Key, c.HoldMs);
                    st.LastFireMs = now;
                    st.Below = 0;
                    Log.Write($"{name}: fired '{c.Key}' at {frac:P1}");
                    Fired?.Invoke(name, frac);
                }
                catch (Exception ex)
                {
                    Log.Write($"{name}: send failed: {ex.Message}");
                }
            }
        }
        else
        {
            st.Below = 0;
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
