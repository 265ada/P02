using System.Diagnostics;

namespace P02;

/// <summary>One globe's latest sample. <paramref name="Ok"/> false means there
/// is no reading, and Note says why.</summary>
public sealed record GlobeReading(string Name, double Fraction, bool Ok,
                                  string Note = "", bool FromText = false,
                                  string TextRaw = "");

public sealed class MonitorEngine : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly KeyPresser _keys = new();
    private readonly TextOcr _ocr = new();
    private readonly GameMemory _mem = new();
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

    /// <summary>The trigger was crossed while disarmed - what would have happened.</summary>
    public event Action<string, double>? WouldFire;

    /// <summary>A watched globe has been reading nothing for a while: the box
    /// is not on the globe. Bool says whether it is currently blind.</summary>
    public event Action<string, bool>? Blind;
    public event Action<bool>? ArmedChanged;

    public MonitorEngine(AppConfig cfg)
    {
        _cfg = cfg;
        _chime = new Chime(cfg.SoundGainDb);
        SyncTextRegions();
        _mem.ProcessName = cfg.GameProcess;
        if (cfg.UseMemory) _mem.Start();
    }

    /// <summary>What the memory reader is doing, for the UI.</summary>
    public string MemoryStatus =>
        !_cfg.UseMemory ? "off" : _mem.Status;

    public bool MemoryFound => _cfg.UseMemory && _mem.Found;

    public bool MemoryEnabled => _cfg.UseMemory;

    /// <summary>Turns memory reading on or off at runtime.</summary>
    public void SetMemory(bool on)
    {
        _cfg.UseMemory = on;
        _mem.ProcessName = _cfg.GameProcess;
        if (on) _mem.Start(); else _mem.Rescan();
    }

    /// <summary>Forces a fresh search.</summary>
    public void RescanMemory() => _mem.Rescan();

    /// <summary>True when Windows can do OCR at all.</summary>
    public bool TextAvailable => _ocr.Available;

    public string TextUnavailable => _ocr.Unavailable;

    /// <summary>Pushes the configured text regions into the reader.</summary>
    public void SyncTextRegions()
    {
        _ocr.Configure("Life", _cfg.Life.UseText && _cfg.Life.Enabled
                               && _cfg.Life.TextRegion.IsValid
            ? _cfg.Life.TextRegion.ToRect() : null);
        _ocr.Configure("Mana", _cfg.Mana.UseText && _cfg.Mana.Enabled
                               && _cfg.Mana.TextRegion.IsValid
            ? _cfg.Mana.TextRegion.ToRect() : null);
    }

    /// <summary>Reads a region once, for the setup button.</summary>
    public string ProbeText(Rectangle r) => _ocr.ProbeOnce(r);

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
    public void TestKey(string key, int holdMs) =>
        _keys.Send(key, holdMs, 1, 40, PostingKeys, GameWindow);

    /// <summary>Posting to the window rather than injecting.</summary>
    private bool PostingKeys =>
        _cfg.InputMethod.Equals("postmessage", StringComparison.OrdinalIgnoreCase);

    /// <summary>Handle of the game window, looked up rarely and cached.</summary>
    private nint GameWindow
    {
        get
        {
            if (_gameWindow != 0 && Native.IsWindowVisible(_gameWindow)) return _gameWindow;
            _gameWindow = Native.FindWindowHandle(_cfg.WindowMatch);
            return _gameWindow;
        }
    }

    private nint _gameWindow;

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
        public long LastWouldFireMs = long.MinValue / 2;
        public long BlindSinceMs;
        public long LastGoodMs = long.MinValue / 2;
        public long LastDisagreeMs = long.MinValue / 2;
        public bool Blind;
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

        // The numbers beside the globe are exact. When there is a recent
        // reading, it decides; the pixels stay as the fallback for the gaps
        // between OCR passes and for anyone who has not set the text region.
        bool fromText = false;
        string textRaw = "";
        bool textConfigured = c.UseText && c.TextRegion.IsValid && _ocr.Available;
        bool textLostOverride = true;
        long textAge = long.MaxValue;

        if (textConfigured && _ocr.TryGet(name, out var tr))
        {
            textAge = _ocr.NowMs - tr.AtMs;
            // The pixels are crude but they are never wildly wrong. A text
            // reading that disagrees with them by this much is a misread, not a
            // correction, so the pixels win and the disagreement is logged.
            if (textAge < 1200 && Math.Abs(tr.Fraction - frac) <= 0.40)
            {
                frac = tr.Fraction;
                fromText = true;
                textRaw = $"{tr.Current:N0}/{tr.Max:N0}";
            }
            else if (textAge < 1200)
            {
                textRaw = $"{tr.Current:N0}/{tr.Max:N0} ignored, pixels say {frac:P0}";
                if (now - st.LastDisagreeMs > 5000)
                {
                    st.LastDisagreeMs = now;
                    Log.Write($"{name}: text says {tr.Fraction:P0} but pixels say {frac:P0}"
                              + $" - ignoring the text ({tr.Current}/{tr.Max})");
                }
            }
        }

        // Memory beats everything on screen when it is working: exact, and
        // free of every way a picture can mislead. The screen stays as the
        // fallback, and as the hint that finds the address in the first place.
        if (_cfg.UseMemory && _mem.TryGet(out var ms) && _mem.NowMs - ms.AtMs < 500)
        {
            double memFrac = name == "Life" ? ms.LifeFraction : ms.ManaFraction;
            int cur = name == "Life" ? ms.CurHp : ms.CurMp;
            int max = name == "Life" ? ms.MaxHp : ms.MaxMp;
            if (max > 0)
            {
                frac = memFrac;
                fromText = true;
                textRaw = $"{cur:N0}/{max:N0} (memory)";
                textLostOverride = false;
            }
        }
        else if (fromText && _cfg.UseMemory)
        {
            // Feed what the screen says back to the search, so it can pick the
            // right candidate out of everything with the same shape.
            if (name == "Life") _mem.HintMaxHp = ParseMax(textRaw);
            else _mem.HintMaxMp = ParseMax(textRaw);
        }

        // The numbers are only drawn on the gameplay screen. Losing them for
        // more than a moment means an inventory, the passive tree, a vendor or
        // the atlas is up - and those cover the globe, so the pixel fallback
        // would be reading the panel and firing at it.
        bool textLost = textConfigured && textAge > c.RequireTextMs && textLostOverride;

        // Anything above the floor is a real reading, and the moment it happens
        // is what separates "nearly dead" from "cannot see it".
        if (frac > c.IgnoreBelow) st.LastGoodMs = now;

        // A watched globe stuck at nothing is the single most common broken
        // setup, and it looks identical to a globe that is simply full: no
        // firing, no complaint. Say it out loud.
        if (frac <= c.IgnoreBelow)
        {
            if (st.BlindSinceMs == 0) st.BlindSinceMs = now;
            // Long enough that being dead or on a loading screen does not
            // trip it, short enough to notice before a fight.
            else if (!st.Blind && now - st.BlindSinceMs > 8000)
            {
                st.Blind = true;
                Log.Write($"{name}: reading {frac:P1} for 8s - the region is not on the globe");
                Blind?.Invoke(name, true);
            }
        }
        else if (st.BlindSinceMs != 0)
        {
            st.BlindSinceMs = 0;
            if (st.Blind) { st.Blind = false; Blind?.Invoke(name, false); }
        }

        double dropRate = st.DropPctPerSec(now, frac);
        st.Push(now, frac);

        // A press that worked shows up as the globe climbing. Anything else is
        // a press that went nowhere, and that is worth saying out loud.
        if (st.Verifying)
        {
            st.MaxSinceFire = Math.Max(st.MaxSinceFire, frac);

            // Every class regenerates, and life and spell leech both refill the
            // globe as well, so any rise at all proves nothing. Only a jump
            // large enough that regen could not have produced it inside the
            // window is worth calling a flask - and even that is a hint, not a
            // fact. Nothing here is allowed to gate firing.
            double rise = st.MaxSinceFire - st.FracAtFire;
            if (rise >= 0.05)
            {
                st.Verifying = false;
                st.NoEffect = 0;
                EffectChecked?.Invoke(name, true, 0);
            }
            else if (now - st.FireMs > c.VerifyWindowMs)
            {
                st.Verifying = false;
                if (rise <= 0.005)
                {
                    st.NoEffect++;
                    Log.Write($"{name}: globe did not move after the press "
                              + $"({st.NoEffect} in a row) - no charges, wrong key, or "
                              + "input not reaching the game");
                }
                else
                {
                    Log.Write($"{name}: globe rose {rise:P1} after the press - too little to "
                              + "tell a flask from regen or leech");
                }
                EffectChecked?.Invoke(name, false, rise <= 0.005 ? st.NoEffect : 0);
            }
        }

        // Reading runs whenever the globe is on, so the UI stays live even
        // while disarmed; only firing is gated below.
        if (!Armed || !focused)
        {
            st.Below = 0;

            // Disarmed is the safe way to check a setup: the trigger point can
            // be confirmed by ear without a single key being sent. Only while
            // disarmed, not merely unfocused, so alt-tabbing at low health does
            // not chirp at you.
            // Reading flat zero means the box is not on the globe at all, so
            // there is nothing to announce. Chirping about an unreadable globe
            // while the firing path silently refuses to act on it made the
            // sound look like proof that keys were being sent.
            if (!Armed && c.Enabled && frac < c.Threshold && !textLost
                && !Unreadable(frac, now, st.LastGoodMs, c))
            {
                if (_cfg.SoundOnFire) _chime.Play(_cfg.SoundGapMs);
                if (now - st.LastWouldFireMs > _cfg.SoundGapMs)
                {
                    st.LastWouldFireMs = now;
                    WouldFire?.Invoke(name, frac);
                }
            }
            return new GlobeReading(name, frac, true, "", fromText, textRaw);
        }

        if (frac >= c.Threshold)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true, "", fromText, textRaw);
        }

        // Nothing goes into a globe we cannot see. The grace period is what
        // makes this safe: a globe that read 60% a second ago and reads 1% now
        // is nearly dead and gets its flask, while one that has read nothing
        // for over a second is a loading screen and gets silence.
        if (textLost || Unreadable(frac, now, st.LastGoodMs, c))
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true,
                                    textLost ? "numbers not on screen" : "", fromText, textRaw);
        }

        // Deep in the red, or dropping fast enough that waiting a full cooldown
        // means dying with charges unspent: press again as soon as the game
        // will accept it, and do not wait for a second confirming frame.
        bool panic = frac < c.PanicBelow || dropRate >= c.FastDropPctPerSec;
        int gap = panic ? c.PanicCooldownMs : c.CooldownMs;
        int confirm = panic ? 1 : Math.Max(1, c.ConfirmFrames);

        st.Below++;
        if (st.Below < confirm || now - st.LastFireMs < gap)
            return new GlobeReading(name, frac, true, "", fromText, textRaw);

        // A burst still going out means the previous request has not even
        // finished leaving; asking for another only builds a backlog.
        if (_keys.Busy)
            return new GlobeReading(name, frac, true, "", fromText, textRaw);

        int shots = Math.Clamp(c.BurstCount, 1, 5);
        if (!_keys.Send(c.Key, c.HoldMs, shots, Math.Clamp(c.BurstGapMs, 5, 500),
                        PostingKeys, GameWindow))
            return new GlobeReading(name, frac, true, "", fromText, textRaw);

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

        return new GlobeReading(name, frac, true, "", fromText, textRaw);
    }

    /// <summary>
    /// Is this a globe we cannot see, rather than one that is nearly empty?
    ///
    /// Both mistakes are bad and they look identical in a single frame. Refuse
    /// too eagerly and it will not fire at 1% life, the moment it matters most.
    /// Refuse too late and it fires into every loading screen. What separates
    /// them is history: a globe that read normally a moment ago and reads
    /// nothing now has just crashed; one that has read nothing for over a
    /// second is not being seen.
    /// </summary>
    internal static bool Unreadable(double frac, long nowMs, long lastGoodMs, WatcherConfig c)
        => frac <= c.IgnoreBelow && nowMs - lastGoodMs > c.BlindGraceMs;

    private static int ParseMax(string raw)
    {
        int slash = raw.IndexOf('/');
        if (slash < 0) return 0;
        int n = 0;
        foreach (char ch in raw.AsSpan(slash + 1))
        {
            if (char.IsAsciiDigit(ch)) n = n * 10 + (ch - '0');
            else if (ch != ',' && ch != '.') break;
            if (n > 1_000_000) return 0;
        }
        return n;
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
        _ocr.Dispose();
        _mem.Dispose();
    }
}
