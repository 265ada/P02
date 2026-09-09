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

    /// <summary>Keys sent since this fight started.</summary>
    public int FiresThisFight { get; private set; }

    /// <summary>Life has dropped recently enough to still count as fighting.</summary>
    public bool InCombat { get; private set; }

    /// <summary>How old the life reading being acted on is, in milliseconds.</summary>
    public long ReadingAgeMs { get; private set; }

    /// <summary>Title of whatever window currently has focus, for the UI.</summary>
    public string ForegroundTitle { get; private set; } = "";

    public event Action<GlobeReading, GlobeReading, GlobeReading, bool>? Sampled;
    public event Action<string, double>? Fired;

    /// <summary>Globe name, whether the last press moved the globe, and how
    /// many in a row have not.</summary>
    public event Action<string, bool, int>? EffectChecked;

    /// <summary>The trigger was crossed while disarmed - what would have happened.</summary>
    public event Action<string, double>? WouldFire;

    /// <summary>A pool's maximum changed and has been adopted: name, old, new.</summary>
    public event Action<string, int, int>? MaxAdopted;

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
            ? _cfg.Life.TextRegion.ToRect() : null,
            _cfg.Life.TextLabel, _cfg.Life.KnownMax);
        _ocr.Configure("Shield", _cfg.Shield.UseText && _cfg.Shield.Enabled
                                 && _cfg.Shield.TextRegion.IsValid
            ? _cfg.Shield.TextRegion.ToRect() : null,
            _cfg.Shield.TextLabel, _cfg.Shield.KnownMax);
        _ocr.Configure("Mana", _cfg.Mana.UseText && _cfg.Mana.Enabled
                               && _cfg.Mana.TextRegion.IsValid
            ? _cfg.Mana.TextRegion.ToRect() : null,
            _cfg.Mana.TextLabel, _cfg.Mana.KnownMax);
    }

    /// <summary>
    /// Levelling and gear move a pool's maximum, and a stated one that has gone
    /// stale refuses every reading in silence. The numbers on screen already
    /// carry the true maximum, so when they have insisted on a different one
    /// for long enough to rule out a misread, take it: update the setting,
    /// point the search at the new value and say so.
    /// </summary>
    /// <summary>
    /// Sweeps a corner in overlapping bands, stopping when it has what it came
    /// for. Bottom first, because that is where the stat block sits and the
    /// chat is above it.
    /// </summary>
    private Dictionary<string, Rectangle> Bands(Rectangle window, Rectangle corner,
                                                string[] labels)
    {
        var found = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);

        int band = Math.Max(110, (int)(window.Height * 0.09));
        int step = band / 2;

        for (int bottom = corner.Bottom; bottom > corner.Top; bottom -= step)
        {
            int top = Math.Max(corner.Top, bottom - band);
            var strip = new Rectangle(corner.X, top, corner.Width, bottom - top);
            if (strip.Height < 24) continue;

            foreach (var (k, v) in _ocr.FindLabelled(strip, labels))
                if (!found.ContainsKey(k)) found[k] = v;

            if (labels.All(found.ContainsKey)) break;
        }

        // Life, Shield and Ward are one stacked block - same left edge, same
        // width, evenly spaced - so finding any of them locates the others.
        // Life is the one that keeps not coming back, and it is the one that
        // matters, so it is placed from Shield when its own word will not read.
        //
        // Across every band rather than inside one: the two lines can easily
        // fall either side of a band edge, and then neither knows about the
        // other.
        if (labels.Contains("Life") && !found.ContainsKey("Life")
            && found.TryGetValue("Shield", out var shield))
        {
            var life = new Rectangle(shield.X, shield.Y - shield.Height,
                                     shield.Width, shield.Height);
            if (life.Y >= corner.Top)
            {
                found["Life"] = life;
                Log.Write($"placed Life at {life} - one line above Shield, which was found; "
                          + "its own word did not come back readable");
            }
        }

        return found;
    }

    /// <summary>Where our own window is, so we never read ourselves.</summary>
    public Rectangle OwnWindow { get; set; }

    /// <summary>Where the readout is, so its own bars are never mistaken for the game's.</summary>
    public Rectangle OverlayBounds { get; set; }

    private static bool Overlaps(Rectangle a, Rectangle b) =>
        a.Width > 0 && a.Height > 0 && a.IntersectsWith(b);

    private int _refinding;
    private long _refoundAtMs = long.MinValue / 2;
    private long _textOkAtMs;

    /// <summary>
    /// Puts the numbers boxes back when they have stopped landing on anything.
    ///
    /// A box is a rectangle on a screen, and screens change: a resolution, a
    /// HUD scale, a different monitor. When it stops covering the numbers it
    /// does not fail loudly - it simply never reads again, and everything
    /// downstream quietly goes back to the globe pixels, which cannot tell a
    /// dead character from a full one. That is what a box sitting 150 pixels
    /// above the life line did, and it sat there through a death.
    ///
    /// Nothing here is a guess: the same label search that set the box in the
    /// first place is run again, and it either finds the words or changes
    /// nothing.
    /// </summary>
    private void RefindLostNumbers(long now, bool focused)
    {
        if (!_ocr.Available) return;

        // Not "only while the game has focus". Somebody with a broken setup is
        // looking at this window, which means the game does not have focus,
        // which means the one thing that would repair it never ran. Reading the
        // screen does not need focus - it needs the game to be drawn and not
        // covered by us.
        if (Native.FindWindowRect(_cfg.WindowMatch) is not { } game) return;
        if (Overlaps(OwnWindow, game)) return;

        // Never set up at all is exactly as worth repairing as set up and
        // broken - more so, since nothing has ever worked.
        bool anythingToDo = _cfg.Life.UseText || _cfg.Mana.UseText;
        if (!anythingToDo) return;

        bool reading = (_ocr.TryGet("Life", out var l) && _ocr.NowMs - l.AtMs < 5000)
                       || (_ocr.TryGet("Mana", out var m) && _ocr.NowMs - m.AtMs < 5000);
        if (reading) { _textOkAtMs = now; return; }

        if (_textOkAtMs == 0) { _textOkAtMs = now; return; }
        if (now - _textOkAtMs < 20000) return;
        if (now - _refoundAtMs < 60000) return;

        _refoundAtMs = now;
        _textOkAtMs = now;

        // On its own thread. This searches whole corners of the screen at
        // several magnifications and takes seconds, and it was running inside
        // the poll loop - so every reading, memory included, stopped dead for
        // as long as it took. On a machine where the numbers need finding
        // often, that is the life value updating once every second or three.
        if (Interlocked.Exchange(ref _refinding, 1) == 1) return;

        Task.Run(() =>
        {
            try
            {
                Log.Write("numbers: nothing read for 20 seconds - looking for the lines again");
                string what = FindAllNumbers();
                Log.Write($"numbers: {what.Replace(Environment.NewLine, " / ")}");
            }
            catch (Exception ex)
            {
                Log.Write($"numbers: looking again failed - {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _refinding, 0);
            }
        });
    }

    private void AdoptChangedMax(string name, WatcherConfig c)
    {
        if (!c.UseText || !c.TextRegion.IsValid) return;

        // Nothing entered: learn it. This is the whole chain - the numbers give
        // the maximum, the maximum lets the memory search find you, and nobody
        // types anything.
        // Whatever the numbers have settled on, whether or not something is
        // stored. A stored maximum that is only replaced when someone notices
        // is a stored maximum that is wrong for the whole of a level.
        int seen = _ocr.StableMaxOf(name);
        if (seen <= 0 || seen == c.KnownMax) return;

        int was = c.KnownMax;
        c.KnownMax = seen;
        Log.Write(was == 0
            ? $"{name}: maximum read as {seen} - filled in"
            : $"{name}: maximum changed from {was} to {seen} - adopted");

        SyncTextRegions();
        if (_cfg.UseMemory) _mem.Rescan();
        MaxAdopted?.Invoke(name, was, seen);
    }

    /// <summary>A maximum the numbers keep showing that disagrees with yours.</summary>
    public int SuggestedMax(string name) => _ocr.SuggestedMax(name);

    /// <summary>
    /// Sets up every stat line it can find in one go: the numbers in the game's
    /// bottom corners, located by their own labels.
    /// </summary>
    public string FindAllNumbers()
    {
        if (!_ocr.Available) return "Windows OCR is not available on this machine.";

        var area = Native.FindWindowRect(_cfg.WindowMatch)
                   ?? (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;

        // Life, shield and ward sit in one corner and mana in the other, so
        // look along the bottom of the game rather than at all of it.
        int w = Math.Max(320, (int)(area.Width * 0.28));
        int h = Math.Max(200, (int)(area.Height * 0.40));
        var left = new Rectangle(area.Left, area.Bottom - h, w, h);
        var right = new Rectangle(area.Right - w, area.Bottom - h, w, h);

        // In bands, from the bottom up, rather than as one tall corner.
        //
        // The chat window lives in the same corner as life, shield and ward,
        // and it is full of text. Handed the whole corner at once, the engine
        // came back with three lines of somebody selling an ascendancy and
        // nothing else - the stat lines are small and pale and simply lost
        // among it. A band a few lines tall cannot be drowned that way.
        var hits = Bands(area, left, ["Life", "Shield"]);
        foreach (var (k, v) in Bands(area, right, ["Mana"])) hits[k] = v;

        // Some layouts put them all together; if mana was not on the right,
        // look where life was.
        if (!hits.ContainsKey("Mana"))
            foreach (var (k, v) in Bands(area, left, ["Mana"])) hits[k] = v;

        // Still no life. Its word will not read on some machines at all, and
        // its neighbour's did not either, so fall back to the shape of the
        // block: in that corner, stacked pairs of numbers sharing a left edge
        // are life, shield and ward, in that order. Nothing else down there is
        // written that way.
        if (!hits.ContainsKey("Life"))
        {
            var stack = _ocr.FindStackedPairs(left);
            if (stack.Count > 0)
            {
                hits["Life"] = stack[0];
                Log.Write($"placed Life at {stack[0]} - the top of {stack.Count} stacked "
                          + "number pairs in that corner; no label could be read");
            }
        }

        var done = new List<string>();
        foreach (var (label, cfg) in new[]
                 { ("Life", _cfg.Life), ("Mana", _cfg.Mana), ("Shield", _cfg.Shield) })
        {
            if (!hits.TryGetValue(label, out var r)) continue;
            cfg.TextRegion = Box.From(r);
            cfg.TextLabel = label;
            cfg.UseText = true;
            done.Add(label);
        }

        SyncTextRegions();
        if (done.Count == 0)
            return "Could not find the numbers. Is the game on screen, with life and mana "
                 + "showing? They are only drawn during play.";

        // Reading each one back is the only way to know it landed on the right
        // line. A box a few pixels out lands on the line below, and life
        // reading the shield value looks perfectly healthy until it kills you.
        var report = new List<string>();
        var seen = new Dictionary<string, int>();

        foreach (var (label, cfg) in new[]
                 { ("Life", _cfg.Life), ("Mana", _cfg.Mana), ("Shield", _cfg.Shield) })
        {
            if (!done.Contains(label)) continue;

            if (!_ocr.VerifyRegion(cfg.TextRegion.ToRect(), label,
                                   out int cur, out int max, out string raw))
            {
                // The box is kept. The search had already read the whole line
                // out of that exact rectangle - "Shield 3,208/3,342" - and this
                // second, weaker read is only being asked to agree. Throwing
                // the box away because a re-read came back short is the same
                // mistake as letting the globe pixels veto the numbers: a worse
                // check discarding a better result, and it left Life and Shield
                // with no box at all after both had just been found.
                report.Add($"{label}: box set, but reading it back gave"
                           + (raw.Length > 0 ? $" only \"{raw}\"" : " nothing")
                           + " - it will be re-read as you play");
                continue;
            }

            seen[label] = max;
            report.Add($"{label}: {cur:N0}/{max:N0}");
        }

        // Two stats reading the same numbers means one box is on the other's
        // line. Life reading shield is the dangerous direction.
        // Two stats reading the same numbers means one box is on the other's
        // line. Life reading shield is the dangerous direction - but the common
        // case is a character with no energy shield at all, where the shield
        // line reads 0/0, is refused as implausible, and the search settles on
        // life instead. Telling somebody to go and drag a box for a stat they
        // do not have is no kind of answer, so the duplicate is simply dropped.
        if (seen.TryGetValue("Life", out int lifeMax)
            && seen.TryGetValue("Shield", out int shieldMax)
            && lifeMax == shieldMax)
        {
            _cfg.Shield.TextRegion = new Box();
            seen.Remove("Shield");
            report.RemoveAll(line => line.StartsWith("Shield:", StringComparison.Ordinal));
            report.Add("Shield: it is reading your life, not a shield - dropped. If you do "
                       + "have energy shield, use Numbers... on the shield row.");
            Log.Write("setup: shield box was on the life line - dropped");
        }

        foreach (var (a, b) in new[] { ("Life", "Mana"), ("Mana", "Shield") })
        {
            if (!seen.TryGetValue(a, out int x) || !seen.TryGetValue(b, out int y)) continue;
            if (x != y) continue;
            report.Add($"WARNING: {a} and {b} are reading the same numbers - one box is on "
                       + "the other's line. Use Numbers... on that panel and drag it yourself.");
        }

        SyncTextRegions();
        return string.Join(Environment.NewLine, report);
    }

    /// <summary>Reads a region once, for the setup button.</summary>
    public string ProbeText(Rectangle r, out Bitmap? shot) => _ocr.ProbeOnce(r, out shot);

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
    /// <summary>The game's window, or 0 when it is not up.</summary>
    internal nint GameWindow
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
        public long LastMemBadMs = long.MinValue / 2;
        public long NoGoodSourceSinceMs;
        public bool HadGoodSource;
        public double LastGoodFrac;
        public bool UberUsed;
        public long ZeroSinceMs;
        public bool LastDitchUsed;
        public long BackoffUntilMs;
        public bool Blind;

        // Per pool, not per app. These were three single fields shared by life,
        // mana and shield: life would confirm its address and mana, sampled a
        // moment later with its own maximum, would immediately unconfirm it -
        // and each pool's disagreement timer reset the others'. Anyone who
        // switched mana on had the two of them cancelling each other out all
        // session.
        public int MemGeneration = -1;
        public bool MemConfirmed;
        public long MemDisagreeSinceMs;
    }

    private void Loop(CancellationToken ct)
    {
        _started = Environment.TickCount64;

        var life = new State();
        var mana = new State();
        var shield = new State();
        var clock = Stopwatch.StartNew();

        long lastUiMs = 0, hzWindowMs = 0, lastLogMs = 0;
        int polls = 0;

        double lastLife = -1;
        long lastDropMs = long.MinValue / 2;

        // Without this the scheduler rounds every sleep up to ~15 ms, which
        // caps the loop near 60 Hz no matter what poll rate is asked for.
        Native.timeBeginPeriod(1);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long t0 = clock.ElapsedMilliseconds;
                bool focused = WindowFocused();

                // Both maxima go to the search every pass, whether or not that
                // globe is switched on. Life alone does not identify the
                // structure - the heap is full of pairs - and mana being off is
                // no reason to withhold what we know about it.
                if (_cfg.UseMemory)
                {
                    // The process behind the window we already found, so a
                    // standalone or Epic install is attached to as readily as
                    // the Steam one.
                    nint gameWnd = Native.FindWindowHandle(_cfg.WindowMatch);
                    if (gameWnd != 0)
                    {
                        Native.GetWindowThreadProcessId(gameWnd, out uint gamePid);
                        if (gamePid != 0) _mem.PreferredPid = (int)gamePid;
                    }

                    _mem.HintMaxHp = ExpectedMax("Life", _cfg.Life, 0);
                    _mem.HintMaxMp = ExpectedMax("Mana", _cfg.Mana, 0);

                    // The currents as well, when the numbers can be read. The
                    // maxima locate the structure; only the current tells the
                    // search which integer beside it is actually yours.
                    // Held for a good while rather than only while fresh. A
                    // search runs at the moment memory was thrown out, which is
                    // exactly when the numbers are most likely to be missing -
                    // so insisting on a reading from the last second meant the
                    // search ran blind and picked the same wrong address again.
                    if (_ocr.TryGet("Life", out var lh) && _ocr.NowMs - lh.AtMs < 20000)
                        _mem.HintCurHp = lh.Current;
                    if (_ocr.TryGet("Mana", out var mh) && _ocr.NowMs - mh.AtMs < 20000)
                        _mem.HintCurMp = mh.Current;
                }

                RefindLostNumbers(t0, focused);

                // Decoration, and rate limited inside, so it can never compete
                // with the reading that decides whether to press a key.
                // Not "while the game has focus". The game keeps drawing while
                // you look at something else, and a screen capture does not
                // care what is focused - so requiring it meant the bar was
                // never looked for the moment you alt-tabbed, and the readout
                // vanished because nothing had found it.
                // Whenever the readout is up, not only while it is following
                // something. Saving a spot needs to know where the character
                // is, and saving a spot is what switches the following on - so
                // requiring it first meant the very first attempt could never
                // work.
                if (_cfg.OverlayOn
                    && Native.FindWindowRect(_cfg.WindowMatch) is { } client)
                {
                    _bar.Ignore = OverlayBounds;
                    _bar.Look(client);
                }

                AdoptChangedMax("Life", _cfg.Life);
                AdoptChangedMax("Mana", _cfg.Mana);
                AdoptChangedMax("Shield", _cfg.Shield);

                var lr = Sample(life, _cfg.Life, "Life", focused, clock);
                var mr = Sample(mana, _cfg.Mana, "Mana", focused, clock);
                var sr = Sample(shield, _cfg.Shield, "Shield", focused, clock);

                // A fight is life going down. Regeneration and leech send it up
                // constantly, so a rise says nothing was hitting you - only a
                // drop does.
                if (lr.Ok && lr.Note.Length == 0)
                {
                    if (lastLife >= 0 && lr.Fraction < lastLife - 0.01) lastDropMs = t0;
                    lastLife = lr.Fraction;
                }

                // Read the numbers harder when anything is near its trigger.
                // Reading flat out all the time is wasted work while healthy,
                // and reading lazily is exactly wrong while dropping.
                bool nearTrouble =
                    (lr.Ok && _cfg.Life.Enabled && lr.Fraction < _cfg.Life.Threshold + 0.15)
                    || (mr.Ok && _cfg.Mana.Enabled && mr.Fraction < _cfg.Mana.Threshold + 0.15)
                    || (sr.Ok && _cfg.Shield.Enabled
                        && sr.Fraction < _cfg.Shield.Threshold + 0.15);
                // Once memory is confirmed it decides everything, and the
                // numbers are only there to catch it pointing at the wrong
                // thing. Reading them several times a second for that is what
                // makes a machine hitch: each read is a screen grab and a
                // recognise. Once a second is plenty to police a liar.
                _ocr.SetInterval(_lifeMemConfirmed ? 900 : nearTrouble ? 60 : 160);

                bool fighting = t0 - lastDropMs < _cfg.CombatGraceMs;
                if (fighting != InCombat)
                {
                    InCombat = fighting;
                    if (!fighting)
                    {
                        if (FiresThisFight > 0)
                            Log.Write($"fight over: {FiresThisFight} press(es) sent");
                        FiresThisFight = 0;
                    }
                }

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
                              (_cfg.Shield.Enabled ? $"  shield {sr.Fraction:P1}" : "") +
                              $"  focused={focused}  hz={ActualHz}  " +
                              $"skipped={_keys.Skipped}  poll={LastPollMs:0.0}ms");
                }

                // The UI cannot use 100 samples a second and repainting that
                // often would slow the loop it is reporting on.
                if (t0 - lastUiMs >= 60)
                {
                    lastUiMs = t0;
                    Sampled?.Invoke(lr, mr, sr, focused);
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
            shield.Cap.Dispose();
        }
    }

    /// <summary>The floating bar over the character, when anyone is asking.</summary>
    private readonly BarFinder _bar = new();

    /// <summary>
    /// Looks for the character right now, ignoring the usual rate limit.
    ///
    /// For the moment somebody saves a spot: the readout is deliberately
    /// sitting on the bars by then, so the caller hides it for an instant and
    /// asks again rather than waiting for a scan that would find nothing.
    /// </summary>
    public Rectangle FindCharacterNow()
    {
        if (Native.FindWindowRect(_cfg.WindowMatch) is not { } client) return Rectangle.Empty;

        _bar.Ignore = OverlayBounds;
        _bar.Look(client, everyMs: 0);
        return CharacterBar;
    }

    /// <summary>Where the character's own life bar is, or empty if it is not up.</summary>
    public Rectangle CharacterBar => _bar.Visible ? _bar.Bar : Rectangle.Empty;

    /// <summary>The run-up to each press, kept so one can be explained later.</summary>
    private readonly FireTrail _trail = new();

    /// <summary>How long the loop has been running, for checks that need warming up.</summary>
    public long UptimeMs => _started == 0 ? 0 : Environment.TickCount64 - _started;

    private long _started;

    /// <summary>Whether memory has an address it is willing to read from.</summary>
    public bool MemoryLocked => _lifeMemConfirmed;

    /// <summary>Whether this pool's numbers have produced a reading recently.</summary>
    public bool NumbersReading(string name) =>
        _ocr.TryGet(name, out var r) && _ocr.NowMs - r.AtMs < 4000;

    /// <summary>Whether life's address is vouched for; the OCR rate follows it.</summary>
    private bool _lifeMemConfirmed;

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

        long now = clock.ElapsedMilliseconds;

        // The globe is the fallback, not a requirement. Without a region this
        // used to stop dead - "no region" - even with the numbers set up and
        // reading perfectly, which is a watcher switched off by the absence of
        // the worst of its three sources.
        // Numbers only means exactly that: the globe is not read, not used as a
        // fallback, and its calibration stops mattering.
        bool haveGlobe = c.Region.IsValid && !_cfg.NumbersOnly;
        double frac = 0;

        if (haveGlobe)
        {
            if (!st.Cap.Grab(c.Region.ToRect()))
                return new GlobeReading(name, 0, false, "capture failed");

            frac = OrbDetector.Fraction(st.Cap.Buffer, st.Cap.Width, st.Cap.Height, c);
        }
        else if (!c.UseText && !_cfg.UseMemory)
        {
            return new GlobeReading(name, 0, false,
                _cfg.NumbersOnly ? "numbers not set up" : "no region");
        }

        // The numbers beside the globe are exact. When there is a recent
        // reading, it decides; the pixels stay as the fallback for the gaps
        // between OCR passes and for anyone who has not set the text region.
        bool fromText = false;
        string textRaw = "";
        bool textConfigured = c.UseText && c.TextRegion.IsValid && _ocr.Available;

        // Kept so memory can be checked against it below. Two sources that
        // both claim to be exact and disagree cannot both be right, and the
        // one printed on your screen is the one that is.
        bool haveOcr = false;
        double ocrFrac = 0;
        int ocrMax = 0;
        bool textLostOverride = true;

        // Asking for memory or for the numbers is asking for the globe pixels
        // NOT to decide. They cannot tell life from energy shield, and they
        // read a poisoned globe - which turns green - as empty, which looks
        // like a killing blow. Falling back to them quietly is how a better
        // source turns into a worse one without saying so.
        bool betterWanted = _cfg.UseMemory || textConfigured || _cfg.NumbersOnly;
        long textAge = long.MaxValue;

        if (textConfigured && _ocr.TryGet(name, out var tr))
        {
            textAge = _ocr.NowMs - tr.AtMs;
            // The pixels are crude but they are never wildly wrong. A text
            // reading that disagrees with them by this much is a misread, not a
            // correction, so the pixels win and the disagreement is logged.
            if (name == "Life") ReadingAgeMs = textAge;

            // The pixels used to be able to veto this, on the theory that they
            // are crude but never wildly wrong. They can be: a globe covered by
            // a panel, or one whose colours no longer separate, reads a flat
            // 100% forever - and it then vetoed 557 correct readings in a
            // single session, including "617/1,490" on the way to a death. The
            // pixels are the fallback. They do not get to overrule the numbers.
            //
            // What the check was actually guarding against - a stray digit
            // turning 1,465 into 11,465 - is caught properly by the maximum,
            // which is read from the same line and has to match. Only when
            // there is no maximum to check against is a second opinion worth
            // anything, and only then are the pixels asked for one.
            bool pixelsMayObject = c.KnownMax <= 0 && haveGlobe;
            bool agrees = !pixelsMayObject || Math.Abs(tr.Fraction - frac) <= 0.40;

            if (textAge < 1200) ocrMax = tr.Max;

            if (textAge < 1200 && agrees)
            {
                frac = tr.Fraction;
                fromText = true;
                haveOcr = true;
                ocrFrac = tr.Fraction;
                textRaw = $"numbers, {tr.Current:N0}/{tr.Max:N0}";
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

        int expectedMax = ExpectedMax(name, c, fromText ? ParseMax(textRaw) : 0);

        // Memory is exact when it is pointed at the right thing, and worthless
        // when it is not. Thousands of pairs in a heap look like a health pool,
        // so it is only believed when it agrees with what we already know.
        if (_cfg.UseMemory && _mem.TryGet(out var ms) && _mem.NowMs - ms.AtMs < 500)
        {
            int cur = name == "Life" ? ms.CurHp : ms.CurMp;
            int max = name == "Life" ? ms.MaxHp : ms.MaxMp;

            double memFrac = name == "Life" ? ms.LifeFraction : ms.ManaFraction;

            // A current of zero beside an intact maximum is a dead pointer, not
            // a dead character - the game frees and rebuilds that structure on
            // a zone change, and the address then reads 0/1,190 forever. It is
            // also the one reading that can never be worth acting on: at zero
            // you are already dead and no flask changes that.
            if (cur <= 0 && max > 0)
            {
                if (st.ZeroSinceMs == 0) st.ZeroSinceMs = now;

                if (now - st.ZeroSinceMs > 1500)
                {
                    st.ZeroSinceMs = 0;
                    st.MemConfirmed = false;
                    if (name == "Life") _lifeMemConfirmed = false;
                    Log.Write($"{name}: memory has read 0 of {max:N0} for over a second - "
                              + "the address has gone stale, searching again");
                    _mem.Rescan();
                }

                textRaw = $"memory reads 0/{max:N0} - stale address, ignored";
                goto pastMemory;
            }

            st.ZeroSinceMs = 0;

            // Every lock is provisional until the numbers have vouched for it
            // once. The old check only ran while an OCR reading was fresh, and
            // between those moments a wrong address had free rein - twenty
            // presses went out at "16%" in the gaps, each one caught and thrown
            // out afterwards, which is no use to anyone already drinking.
            if (st.MemGeneration != _mem.Generation)
            {
                st.MemGeneration = _mem.Generation;
                st.MemConfirmed = false;
                st.MemDisagreeSinceMs = 0;
            }

            // Agreeing with the configured maximum is not enough on its own:
            // if that maximum was itself adopted from a misread, the search was
            // pointed at the wrong number to begin with and will happily find
            // something that matches it. The numbers printed on screen are the
            // only independent witness, so when they are readable they get the
            // final say.
            bool matchesOcr = !haveOcr || Math.Abs(memFrac - ocrFrac) <= 0.15;

            // An address is taken as good once its maximum is the one we asked
            // the search to find - which is also what the search matched on, and
            // what it re-checks every read.
            //
            // It used to need the numbers to agree with it first, and that
            // deadlocked: while the numbers are misreading, they never agree,
            // so the address stays unconfirmed, so the misreads keep winning.
            // That is precisely the state where memory is the only thing still
            // telling the truth, and it was the one state where it had no say.
            // Two independent sources agreeing on a maximum that is not the one
            // configured settles it outright. A stale maximum is not a small
            // problem: every reading is measured against it, the numbers refuse
            // anything that disagrees, and memory will not confirm an address
            // whose maximum is "wrong" - so the whole thing goes blind at
            // exactly the moment a level or a gear swap changed it. That is
            // what 1,490 becoming 1,503 did.
            if (max > 0 && ocrMax > 0 && max == ocrMax && expectedMax > 0
                && max != expectedMax)
            {
                Log.Write($"{name}: memory and the numbers both read a maximum of {max:N0}, "
                          + $"not {expectedMax:N0} - adopting it at once");
                int wasBoth = expectedMax;
                c.KnownMax = max;
                expectedMax = max;
                _cfg.Save();
                MaxAdopted?.Invoke(name, wasBoth, max);
            }

            // The numbers are the authority on the maximum, because they are
            // read from the screen and cannot be pointed at the wrong thing. A
            // memory maximum that disagrees with a maximum being read right now
            // is a wrong address, whatever its current value looks like - and
            // "reading HP 754/1217" beside a game saying 0/1,151 is exactly
            // that.
            // A confirmed address whose maximum has moved is a level, not a
            // wrong address - the structure does not move when you level. Take
            // the new value from it and carry on; nothing needs reading and
            // nothing needs finding again.
            if (st.MemConfirmed && max > 0 && expectedMax > 0 && max != expectedMax
                && ocrMax <= 0)
            {
                Log.Write($"{name}: maximum is now {max:N0} where it was {expectedMax:N0} - "
                          + "taken from the address already locked");
                int wasLevelled = expectedMax;
                c.KnownMax = max;
                expectedMax = max;
                _cfg.Save();
                MaxAdopted?.Invoke(name, wasLevelled, max);
            }

            if (ocrMax > 0 && max > 0 && max != ocrMax)
            {
                if (st.MemConfirmed)
                {
                    st.MemConfirmed = false;
                    if (name == "Life") _lifeMemConfirmed = false;
                    Log.Write($"{name}: memory says the maximum is {max:N0} but the numbers "
                              + $"say {ocrMax:N0} - wrong address, searching again");
                    _mem.Rescan();
                }

                textRaw = $"memory max {max:N0}, numbers say {ocrMax:N0} - ignored";
                goto pastMemory;
            }

            if (!st.MemConfirmed && max > 0 && expectedMax > 0 && max == expectedMax)
            {
                st.MemConfirmed = true;
                if (name == "Life") _lifeMemConfirmed = true;
                Log.Write($"{name}: memory has the right maximum ({max:N0}) - using this "
                          + "address until the numbers disagree with it for two seconds");
            }

            // Once the numbers have vouched for an address, they stop being the
            // authority over it, because they are the less reliable of the two
            // and fail differently. Memory reads the variable itself and can
            // only be wrong by pointing somewhere wrong - which is what
            // confirmation rules out, once. The numbers are guessed from pixels
            // and drop or invent a digit now and then: "490/1,490" is 1,490
            // with the leading 1 lost, and it read as 33% and fired three
            // times while the pool was full.
            //
            // A wrong address disagrees forever; a misread disagrees for a
            // frame. So disagreement is timed rather than acted on, and only a
            // steady one costs the lock.
            // A wrong address disagrees forever and loses the lock; a misread
            // disagrees for a frame or two and is ridden out. This is the only
            // thing that can take an address away, so it is also what protects
            // against the search having found the wrong one.
            if (st.MemConfirmed && haveOcr && !matchesOcr)
            {
                if (st.MemDisagreeSinceMs == 0) st.MemDisagreeSinceMs = now;

                if (now - st.MemDisagreeSinceMs > 2000)
                {
                    st.MemConfirmed = false;
                    st.MemDisagreeSinceMs = 0;
                    if (name == "Life") _lifeMemConfirmed = false;
                    Log.Write($"{name}: memory has read {memFrac:P0} against the numbers' "
                              + $"{ocrFrac:P0} for two seconds - dropping this address");
                    _mem.Rescan();
                }
                else
                {
                    // Believe memory through the wobble, and say so, so a
                    // rejected frame is never mistaken for a healthy one.
                    textRaw = $"memory {memFrac:P0}, numbers misread {ocrFrac:P0}";
                    frac = memFrac;
                    fromText = true;
                    textLostOverride = false;
                    haveOcr = false;
                }
            }
            else if (matchesOcr)
            {
                st.MemDisagreeSinceMs = 0;
            }

            bool trusted = max > 0 && (expectedMax == 0 || max == expectedMax)
                           && matchesOcr && st.MemConfirmed;


            if (trusted)
            {
                frac = name == "Life" ? ms.LifeFraction : ms.ManaFraction;
                fromText = true;
                textRaw = $"memory, {name.ToLowerInvariant()} {cur:N0}/{max:N0}";
                textLostOverride = false;
            }
            // Only when the maxima genuinely differ. This used to fire whenever
            // the address was merely unconfirmed, and logged "memory found max
            // 1490 but the max is 1490 - wrong structure", then threw the
            // address away and found the same one again ten seconds later,
            // forever. Memory was never once used.
            else if (max > 0 && expectedMax > 0 && max != expectedMax && matchesOcr)
            {
                textRaw = $"memory says max {max:N0}, yours is {expectedMax:N0} - ignored";
                if (now - st.LastMemBadMs > 10000)
                {
                    st.LastMemBadMs = now;
                    Log.Write($"{name}: memory found max {max} but the max is {expectedMax}"
                              + " - wrong structure, searching again");
                    _mem.Rescan();
                }
            }
        }

        pastMemory:

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
                Log.Write($"{name}: reading {frac:P1} for 8 seconds - "
                          + (_cfg.NumbersOnly
                              ? "nothing is being read; press Set it up for me"
                              : "the region is not on the globe"));
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
                st.BackoffUntilMs = 0;
                EffectChecked?.Invoke(name, true, 0);
            }
            else if (now - st.FireMs > c.VerifyWindowMs)
            {
                st.Verifying = false;
                if (rise <= 0.005)
                {
                    st.NoEffect++;
                    if (st.NoEffect >= Math.Max(1, c.NoEffectBefore))
                    {
                        st.BackoffUntilMs = now + c.NoEffectBackoffMs;
                        Log.Write($"{name}: {st.NoEffect} presses changed nothing - "
                                  + $"pausing {c.NoEffectBackoffMs} ms rather than spending "
                                  + "more charges on nothing");
                    }
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
                if (_cfg.SoundOnFire && _cfg.SoundWhenDisarmed) _chime.Play(_cfg.SoundGapMs);
                if (now - st.LastWouldFireMs > _cfg.SoundGapMs)
                {
                    st.LastWouldFireMs = now;
                    WouldFire?.Invoke(name, frac);
                }
            }
            return new GlobeReading(name, frac, true, "", fromText, textRaw);
        }

        // Nothing goes into a globe we cannot see. The grace period is what
        // makes this safe: a globe that read 60% a second ago and reads 1% now
        // is nearly dead and gets its flask, while one that has read nothing
        // for over a second is a loading screen and gets silence.
        // fromText covers memory as well: it means something exact decided.
        if (fromText)
        {
            st.NoGoodSourceSinceMs = 0;
            st.HadGoodSource = true;
            st.LastGoodFrac = frac;
        }
        else if (betterWanted)
        {
            if (st.NoGoodSourceSinceMs == 0) st.NoGoodSourceSinceMs = now;

            // The grace exists so one missed frame does not block a heal. It
            // was letting the globe pixels decide during it, which is how a
            // loading screen got to fire: the numbers are gone, the pixels read
            // whatever is on screen, and two seconds is long enough to act on
            // it. Carry the last exact reading through the gap instead. Stale
            // and real beats fresh and wrong.
            if (st.HadGoodSource)
            {
                frac = st.LastGoodFrac;
                textRaw = $"last known {frac:P0}";
            }
        }

        // The grace period is for a source that was working and dropped out for
        // a moment - a heal should not wait on one missed frame. A source that
        // has never once produced a reading is not having a hiccup, it is not
        // set up, and there is nothing to be patient about: the pixels must not
        // stand in for it even briefly. A globe turning green is a drop from
        // full to nothing in one frame, and two seconds is long enough to spend
        // every charge on it.
        bool sourceLost = betterWanted && !fromText
                          && (!st.HadGoodSource
                              || now - st.NoGoodSourceSinceMs > c.ActOnStaleMs);

        bool blind = Unreadable(frac, now, st.LastGoodMs, c);

        // Every poll, into memory only. This is what makes "it fired and it
        // should not have" answerable: the press itself says almost nothing,
        // and by the time it is logged the run-up has gone.
        _trail.Note($"{name} {frac:P1} from {(fromText ? textRaw : "globe pixels")} "
                    + $"age {(textAge == long.MaxValue ? -1 : textAge)}ms  "
                    + $"armed={Armed} focused={focused}  "
                    + $"refuse={(textLost ? "numbers gone" : sourceLost ? "no exact reading" : blind ? "globe unreadable" : "no")}  "
                    + $"fire<{c.Threshold:P0} panic<{c.PanicBelow:P0} emergency<{c.UberBelow:P0}  "
                    + $"below={st.Below} sinceFire={now - st.LastFireMs}ms "
                    + $"backoff={Math.Max(0, st.BackoffUntilMs - now)}ms uberUsed={st.UberUsed}");

        // Zero is never worth acting on. Either you are dead, in which case a
        // flask is beside the point, or something has broken - a freed address,
        // a misread, a globe behind a loading screen - in which case pressing
        // is worse than doing nothing. Both his machine and mine have spent an
        // evening firing at "0".
        bool zero = frac <= 0.0005;

        if (sourceLost || textLost || blind || zero)
        {
            st.Below = 0;
            string why = textLost ? "numbers not on screen"
                       : sourceLost ? "no exact reading yet"
                       : zero ? "reads zero - dead, or the reading has broken"
                       : "cannot read the globe";
            return new GlobeReading(name, frac, true, why, fromText, textRaw);
        }

        // The safety net: one press, once, when you fall past the floor.
        //
        // It sits below the readability guard on purpose. It used to sit above
        // it, which meant it was the one path that could fire on a reading the
        // rest of the code had already decided not to trust - so opening the
        // atlas, where the numbers are gone and the globe pixels read low,
        // pressed a flask. A net that fires when nothing is falling is not a
        // net.
        //
        // Everything else here can be waiting - a cooldown running, a burst
        // still going out, a confirming frame not yet counted - and that is
        // where a heal gets missed. This does not care what is waiting and does
        // not repeat: it fires a single press and then stays quiet until you
        // have climbed back out, so it is a net rather than a second trigger
        // spending charges alongside the first.
        if (frac > 0)
        {
            if (Net(c.UberBelow, ref st.UberUsed, "EMERGENCY")) 
                return new GlobeReading(name, frac, true, "", fromText, textRaw);

            // A second line further down. Each net is a single press, so the
            // first one having gone does not help if it landed in a cooldown or
            // on a flask with nothing left - and by then there is no other
            // chance coming.
            if (Net(c.LastDitchBelow, ref st.LastDitchUsed, "LAST DITCH"))
                return new GlobeReading(name, frac, true, "", fromText, textRaw);
        }

        if (frac >= c.Threshold)
        {
            st.Below = 0;
            return new GlobeReading(name, frac, true, "", fromText, textRaw);
        }

        bool Net(double floor, ref bool used, string what)
        {
            if (floor <= 0) return false;
            if (frac > floor + 0.05) used = false;
            if (frac > floor || used || _keys.Busy) return false;
            if (!_keys.Send(c.Key, c.HoldMs, 1, 40, PostingKeys, GameWindow)) return false;

            used = true;
            st.LastFireMs = now;
            st.Below = 0;
            FiresThisFight++;
            if (_cfg.SoundOnFire) _chime.Play(_cfg.SoundGapMs);
            Log.Write($"{name}: '{c.Key}' x1 at {frac:P1} {what} - one press, "
                      + "will not repeat until recovered");
            _trail.Dump($"{name} {what.ToLowerInvariant()} press at {frac:P1}, under the "
                        + $"{floor:P0} floor, reading from "
                        + $"{(fromText ? textRaw : "globe pixels")}");
            Fired?.Invoke(name, frac);
            return true;
        }

        // Deep in the red, or dropping fast enough that waiting a full cooldown
        // means dying with charges unspent: press again as soon as the game
        // will accept it, and do not wait for a second confirming frame.
        bool panic = frac < c.PanicBelow || dropRate >= c.FastDropPctPerSec;
        int gap = panic ? c.PanicCooldownMs : c.CooldownMs;
        int confirm = panic ? 1 : Math.Max(1, c.ConfirmFrames);

        // Presses are doing nothing: no charges, or they are not arriving.
        // Either way, more of them will not help, so wait instead of emptying
        // what is left into a flask that cannot use it.
        if (now < st.BackoffUntilMs)
            return new GlobeReading(name, frac, true, "no effect - waiting", fromText, textRaw);

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
        FiresThisFight++;

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

        Log.Write($"{name}: '{c.Key}' x{shots} at {frac:P1}"
                  + (panic ? $" PANIC (drop {dropRate:0}%/s)" : ""));
        _trail.Dump($"{name} x{shots} at {frac:P1}, under the {c.Threshold:P0} trigger"
                    + (panic ? $", panicking (dropping {dropRate:0}%/s)" : "")
                    + $", reading from {(fromText ? textRaw : "globe pixels")}");
        Fired?.Invoke(name, frac);

        return new GlobeReading(name, frac, true, "", fromText, textRaw);
    }

    /// <summary>What a watcher should act on, and whether it should act at all.</summary>
    internal readonly record struct SourceChoice(double Frac, bool Hold, bool Exact);

    /// <summary>
    /// Decides which reading a watcher uses when an exact source is wanted.
    ///
    /// The globe pixels must never stand in for memory or the numbers. They
    /// cannot tell life from energy shield, they read a recoloured globe as
    /// empty, and on a loading screen they read the loading screen. The grace
    /// period exists so one missed frame does not block a heal - so it carries
    /// the last exact reading through the gap rather than handing the decision
    /// to the pixels for two seconds, which is long enough to act on nonsense.
    /// </summary>
    internal static SourceChoice ChooseSource(bool betterWanted, bool exactNow,
                                              double exactFrac, double pixelFrac,
                                              bool hadExact, double lastExactFrac,
                                              long sinceMs, long nowMs, int graceMs)
    {
        if (!betterWanted) return new SourceChoice(pixelFrac, false, false);
        if (exactNow) return new SourceChoice(exactFrac, false, true);

        // Never had one: not a hiccup, not set up. Nothing to carry, and the
        // pixels do not get to fill in.
        if (!hadExact) return new SourceChoice(pixelFrac, true, false);

        // The carried value is shown either way, but it is only acted on
        // briefly. A missed frame is one interval; a loading screen is seconds,
        // and a value frozen from before the load kept firing right through it.
        bool tooOld = nowMs - sinceMs > graceMs;
        return new SourceChoice(lastExactFrac, tooOld, false);
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

    /// <summary>
    /// The maximum for a pool: what you typed, else what the numbers on screen
    /// last said, else nothing.
    /// </summary>
    private int ExpectedMax(string name, WatcherConfig c, int fromRaw)
    {
        if (c.KnownMax > 0) return c.KnownMax;
        if (fromRaw > 0) return fromRaw;
        if (c.UseText && c.TextRegion.IsValid && _ocr.TryGet(name, out var t)) return t.Max;
        return 0;
    }

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
