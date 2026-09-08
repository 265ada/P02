using System.Text.Json;
using System.Text.Json.Serialization;

namespace P02;

public sealed class Box
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    [JsonIgnore]
    public bool IsValid => Width > 3 && Height > 3;

    public Rectangle ToRect() => new(Left, Top, Width, Height);

    public static Box From(Rectangle r) => new()
        { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height };

    public override string ToString() => IsValid
        ? $"{Width}x{Height} at {Left},{Top}"
        : "not set";
}

/// <summary>One globe being watched, with its own key and trigger.</summary>
public sealed class WatcherConfig
{
    public bool Enabled { get; set; }
    public Box Region { get; set; } = new();

    /// <summary>
    /// The "1,465/1,465" text beside the globe. When set, this is the reading
    /// that decides firing: it is an exact ratio, needs no calibration, and
    /// reads the maximum too, so gear and buffs that move your pool change
    /// nothing. The globe pixels stay as the fallback.
    /// </summary>
    public Box TextRegion { get; set; } = new();

    public bool UseText { get; set; } = true;

    /// <summary>
    /// The word beside the numbers, used to pick the right line. A box drawn
    /// around the life numbers almost always catches shield and ward as well,
    /// and those are number pairs too - ward at 90/90 read as life is a misfire
    /// waiting to happen. Include the label in the box and this settles it.
    /// </summary>
    public string TextLabel { get; set; } = "";

    /// <summary>
    /// Your maximum for this pool, or 0 to work it out automatically.
    ///
    /// Memory holds thousands of number pairs that look exactly like a health
    /// pool - ward at 90/90 is indistinguishable from life at 90/90 - so the
    /// search needs something to aim at. Given a maximum it can pick the right
    /// one; without one it is guessing, and it guessed wrong.
    /// </summary>
    public int KnownMax { get; set; }

    /// <summary>
    /// Once a text region is set, how long its numbers may be unreadable before
    /// P02 stops acting at all.
    ///
    /// The numbers vanish on every screen that is not gameplay - inventory, the
    /// passive tree, a vendor, the atlas - and those screens also cover the
    /// globe, so the pixel fallback reads the panel instead and fires at it.
    /// Losing the numbers is the clearest signal there is that we are not
    /// looking at the game, so it is treated as one. Short gaps still fall back
    /// to pixels, since OCR misses the odd frame.
    /// </summary>
    public int RequireTextMs { get; set; } = 2000;

    /// <summary>
    /// How stale an exact reading may be and still be acted on.
    ///
    /// Short on purpose. A missed OCR frame is one interval; a loading screen
    /// is seconds. Carrying a reading for two seconds meant portalling out at
    /// low life kept firing at a value frozen from before the load.
    /// </summary>
    public int ActOnStaleMs { get; set; } = 400;

    /// <summary>
    /// Stop pressing for this long after several presses in a row change
    /// nothing.
    ///
    /// P02 cannot see your charges, so without this it keeps pressing into an
    /// empty flask - which is where "it ate everything instantly and then did
    /// nothing" comes from. If a press does not move the pool, more presses
    /// will not either, so it waits and lets charges come back.
    /// </summary>
    public int NoEffectBackoffMs { get; set; } = 3000;

    /// <summary>Presses in a row that must do nothing before backing off.</summary>
    public int NoEffectBefore { get; set; } = 3;

    /// <summary>Fire when the globe falls below this fraction (0-1).</summary>
    public double Threshold { get; set; } = 0.50;

    public string Key { get; set; } = "1";
    /// <summary>
    /// How long the key is held down. A game reads input once a frame, so a
    /// press shorter than a frame can go down and back up between two of them
    /// and never be seen: 20 ms is invisible below about 50 fps. 70 ms spans a
    /// frame down to 14 fps.
    /// </summary>
    public int HoldMs { get; set; } = 70;

    /// <summary>Normal gap between presses while sitting below the trigger.</summary>
    public int CooldownMs { get; set; } = 350;

    /// <summary>Consecutive low reads required, so one odd frame can't fire.</summary>
    public int ConfirmFrames { get; set; } = 2;

    // --- emergency response ---------------------------------------------

    /// <summary>
    /// What counts as "no reading". A globe sitting at or under this for longer
    /// than <see cref="BlindGraceMs"/> is one we cannot see - a loading screen,
    /// a death screen, something covering the corner - and nothing is sent into
    /// it.
    /// </summary>
    public double IgnoreBelow { get; set; } = 0.02;

    /// <summary>
    /// How long a globe may read nothing before it counts as unreadable rather
    /// than nearly empty.
    ///
    /// This exists because both mistakes are bad and they look identical in a
    /// single frame. Refuse too eagerly and it will not fire at 1% life, which
    /// is the moment it matters most. Refuse too late and it fires into every
    /// loading screen. A globe that read 60% a second ago and reads 1% now is
    /// nearly dead; one that has read nothing for over a second is not being
    /// seen at all.
    /// </summary>
    public int BlindGraceMs { get; set; } = 1200;

    /// <summary>Below this fraction, switch to the short gap and fire on sight.</summary>
    public double PanicBelow { get; set; } = 0.30;

    /// <summary>
    /// The safety net: one press when you fall past this, whatever else is
    /// waiting, and then nothing until you have climbed back out.
    ///
    /// It exists because the ordinary path can be mid-cooldown, mid-burst or a
    /// frame short of confirming at the moment it is needed, and that is when a
    /// heal gets missed. It deliberately does not repeat - a net, not a second
    /// trigger spending charges alongside the first.
    ///
    /// Capped at 30%. Above that it stops being an emergency and becomes a way
    /// to spend charges on chip damage, leaving none for the hit that matters.
    /// </summary>
    public double UberBelow { get; set; } = 0.15;

    /// <summary>Gap between presses while panicking. Charges allow this.</summary>
    public int PanicCooldownMs { get; set; } = 60;

    /// <summary>Losing more than this many percent per second also counts as
    /// panic, even if you are still above PanicBelow. A big hit is caught on
    /// the way down instead of after it lands.</summary>
    public double FastDropPctPerSec { get; set; } = 30;

    // --- did it actually work? -------------------------------------------
    // Flasks recover over a duration and the recovery stops the moment the
    // resource is full, so a press that works shows up as the globe rising
    // within a second. A press that changes nothing means no charges, the
    // wrong key, or input not reaching the game - and nothing else in P02 can
    // tell those apart from a press that simply had no room to heal.

    /// <summary>
    /// Watch the globe after firing. Only ever a hint: every class regenerates,
    /// and life and spell leech refill the globe too, so a rise cannot prove a
    /// flask fired. It never gates firing.
    /// </summary>
    public bool VerifyEffect { get; set; } = true;

    /// <summary>How long to wait for the globe to start rising.</summary>
    public int VerifyWindowMs { get; set; } = 900;

    /// <summary>Presses sent per trigger. Raise if one charge is not enough.</summary>
    public int BurstCount { get; set; } = 1;

    public int BurstGapMs { get; set; } = 30;

    // --- pixel classification -------------------------------------------
    /// <summary>"red" for life, "blue" for mana.</summary>
    public string Hue { get; set; } = "red";
    /// <summary>How far the hue's channel must lead the other two, 0-255.
    /// An absolute margin survives the pale washed-out centre of the mana
    /// globe, where a ratio test collapses.</summary>
    public int ColourMargin { get; set; } = 30;

    public int MinValue { get; set; } = 50;
    public double BandFraction { get; set; } = 0.30;
    public double RowThreshold { get; set; } = 0.55;
    public int MinRun { get; set; } = 4;

    /// <summary>Box row the liquid reaches when the globe is full, and the row
    /// just past the bottom of the liquid. Set by "Full = 100%". Without these
    /// the box edges stand in for the globe, and any frame caught in the box
    /// makes a full globe read low.</summary>
    public int FullRow { get; set; } = -1;

    public int EmptyRow { get; set; } = -1;

    // --- learned from calibration ---------------------------------------
    // A hue test alone cannot separate a full globe from an empty one: the
    // empty part of the life globe is the same red, only darker. These record
    // what full and empty actually look like in your box so the threshold can
    // be put between them instead of guessed.

    /// <summary>Colour lead over the other channels, low end of the liquid.</summary>
    public int FullDominance { get; set; } = -1;

    /// <summary>Brightness of the hue channel, low end of the liquid.</summary>
    public int FullValue { get; set; } = -1;

    /// <summary>Colour lead, high end of the drained globe.</summary>
    public int EmptyDominance { get; set; } = -1;

    /// <summary>Brightness of the hue channel, high end of the drained globe.</summary>
    public int EmptyValue { get; set; } = -1;

    /// <summary>
    /// Judge the globe by brightness alone, ignoring its colour.
    ///
    /// The life globe is not always red: poison and other debuffs recolour it,
    /// and a green globe has no red dominance at all - so a hue test reads it
    /// as empty the instant the colour changes. That is not a slow drift, it is
    /// a jump from full to nothing, which looks exactly like a killing blow and
    /// fires accordingly.
    ///
    /// Brightness survives a recolour: the liquid is bright whatever colour it
    /// has been turned, and the drained part stays dark.
    /// </summary>
    public bool IgnoreHue { get; set; }

    /// <summary>Count near-white pixels as liquid.
    /// Off by default: the life globe has a bright rune drawn across it that is
    /// near-white, so this made an empty globe read almost full. The globes carry a specular
    /// highlight that is not blue or red at all, and it would otherwise punch a
    /// hole in the middle of the mask.</summary>
    public bool GlareIsLiquid { get; set; }
}

public sealed class AppConfig
{
    public WatcherConfig Life { get; set; } = new()
        { Hue = "red", Key = "1", Threshold = 0.50, TextLabel = "Life" };

    public WatcherConfig Mana { get; set; } = new()
        { Hue = "blue", Key = "2", Threshold = 0.30, PanicBelow = 0.15, TextLabel = "Mana" };

    /// <summary>Screen samples per second. 5-250; the loop reports what it
    /// actually achieved next to this in the UI.</summary>
    /// <summary>
    /// Energy shield, off by default. Nothing recovers it from a flask unless
    /// you have taken something that says so - Eternal Youth and its kin - so
    /// firing at it is waste for most characters and the point for a few.
    ///
    /// It shares the life flask's key and timing, because that is the flask
    /// that recovers it.
    /// </summary>
    public WatcherConfig Shield { get; set; } = new()
        { Hue = "red", Key = "1", Threshold = 0.50, TextLabel = "Shield" };

    /// <summary>
    /// How long life must go without dropping before a fight counts as over.
    /// Only drops count as fighting: every class regenerates, so a rise means
    /// nothing is hitting you.
    /// </summary>
    public int CombatGraceMs { get; set; } = 8000;

    public int PollHz { get; set; } = 60;

    /// <summary>Only act while the focused window title contains this. Blank = any.</summary>
    public string WindowMatch { get; set; } = "Path of Exile";

    public string ArmHotkey { get; set; } = "F8";

    /// <summary>
    /// "sendinput" injects into the system; "postmessage" posts straight to the
    /// game window. Injected input is what most games read, but it can be
    /// missed; posted messages reach a window without focus, and some games
    /// ignore them entirely because they never touch real keyboard state.
    /// Which works is a matter for testing, so both are here.
    /// </summary>
    public string InputMethod { get; set; } = "sendinput";

    /// <summary>
    /// Read life and mana from the game's memory instead of from the screen.
    /// Exact and instant, and by far the most intrusive thing here - reading
    /// another process is what anti-cheat looks for, where watching the screen
    /// is passive. Off unless you turn it on.
    /// </summary>
    public bool UseMemory { get; set; }

    /// <summary>Process to read, without ".exe".</summary>
    public string GameProcess { get; set; } = "PathOfExileSteam";

    /// <summary>Short ding when a key is fired.</summary>
    public bool SoundOnFire { get; set; } = true;

    /// <summary>
    /// Minimum gap between dings. Firing can repeat several times a second, so
    /// this is what keeps it a signal rather than a machine gun.
    /// </summary>
    public int SoundGapMs { get; set; } = 6000;

    /// <summary>Boost over the original ding level, in dB. 0 is unchanged.</summary>
    public int SoundGainDb { get; set; }

    /// <summary>
    /// Also ding while disarmed, when it would have fired. Off: it sounds
    /// exactly like a real press, which makes "did it fire?" harder to answer
    /// rather than easier - and disarmed is the state people leave it in.
    /// </summary>
    public bool SoundWhenDisarmed { get; set; }

    /// <summary>Where the window was last time, so it comes back as you left it.</summary>
    public int WindowX { get; set; } = -1;

    public int WindowY { get; set; } = -1;

    /// <summary>The small always-on-top readout: shown, and where it sits.</summary>
    public bool OverlayOn { get; set; }

    public int OverlayX { get; set; } = -1;

    public int OverlayY { get; set; } = -1;
    public bool StartMinimised { get; set; }
    public bool CheckUpdatesOnStart { get; set; } = true;

    /// <summary>
    /// Install an update without being asked, when only a few releases behind.
    ///
    /// Being behind is what killed this twice: fixes sat on a server while the
    /// version they fixed kept playing. A jump of a release or three is a
    /// change small enough to have been read about; a bigger one is a decision
    /// worth making rather than having made for you.
    /// </summary>
    public bool AutoInstall { get; set; } = true;

    public int AutoInstallMaxBehind { get; set; } = 3;

    /// <summary>
    /// Set only while restarting for an update, so it comes back armed if it
    /// was armed. Every other launch starts disarmed on purpose - a monitor
    /// that arms itself when you did not ask is a monitor firing into a game
    /// you have not looked at yet.
    /// </summary>
    public bool ResumeArmed { get; set; }

    /// <summary>
    /// Hide P02's windows from screen capture so they cannot be read as a
    /// globe. Off, and staying off: it also hides them from screenshots, the
    /// Snipping Tool, Discord and OBS.
    /// </summary>
    public bool HideFromCapture { get; set; }

    /// <summary>Bumped when a stored setting needs repairing on load.</summary>
    public int SettingsVersion { get; set; }

    /// <summary>What Repair() changed, for the UI to show once.</summary>
    [JsonIgnore]
    public List<string> Repairs { get; } = [];

    /// <summary>
    /// Fixes settings that cannot work, rather than leaving them to fail
    /// silently in a fight. Both of these shipped as defaults or as advice from
    /// the app itself, so they are not the user's doing.
    /// </summary>
    public void Repair()
    {
        // Each fix has its own version gate. A single gate meant a settings
        // file already stamped by an earlier release skipped every later fix -
        // which is how the label repair never ran, leaving the numbers picked
        // by position and reading ward as life.
        const int Current = 6;
        int was = SettingsVersion;
        if (was >= Current) return;

        var globes = new[] { ("Life", Life), ("Mana", Mana), ("Shield", Shield) };

        if (was < 4)
        {
            if (HideFromCapture)
            {
                HideFromCapture = false;
                Repairs.Add("Turned off hiding from screen capture - it also hid the window "
                            + "from screenshots and screen sharing.");
            }

            foreach (var (name, w) in globes)
            {
                if (w.GlareIsLiquid)
                {
                    w.GlareIsLiquid = false;
                    Repairs.Add($"{name}: stopped counting bright pixels as liquid - the rune "
                                + "on the globe is near-white and read as full.");
                }

                // A margin this low accepts the drained globe, which is the
                // same hue as the liquid and only darker.
                if (w.ColourMargin < 10 && w.EmptyDominance < 0)
                {
                    Repairs.Add($"{name}: colour margin was {w.ColourMargin}, low enough that "
                                + "an empty globe read as full. Reset to 30.");
                    w.ColourMargin = 30;
                }

                if (w.HoldMs < 40)
                {
                    Repairs.Add($"{name}: key was held for {w.HoldMs} ms, short enough for the "
                                + "game to miss it between frames. Raised to 70 ms.");
                    w.HoldMs = 70;
                }
            }
        }

        if (was < 5)
        {
            foreach (var (name, w) in globes)
            {
                if (w.TextLabel.Length != 0) continue;
                w.TextLabel = name;
                Repairs.Add($"{name}: the numbers had no label to look for, so whichever line "
                            + $"came first was used - ward included. Now anchored on \"{name}\".");
            }
        }

        if (was < 6)
        {
            // Two settings that were reasonable guesses and turned out to cost
            // more than they gave.
            foreach (var (name, w) in globes)
            {
                if (w.HoldMs <= 150) continue;
                Repairs.Add($"{name}: key was held for {w.HoldMs} ms. That does throttle "
                            + "firing, but only because each press takes that long to leave - "
                            + "and it slowed the emergency press down with it. Set to 70 ms; "
                            + "Cooldown is the rate limit.");
                w.HoldMs = 70;
            }

            if (PollHz < 40)
            {
                Repairs.Add($"Polls per second was {PollHz}, which is "
                            + $"{1000 / Math.Max(1, PollHz)} ms of delay before a change is "
                            + "even looked at. Raised to 60.");
                PollHz = 60;
            }
        }

        SettingsVersion = Current;
        foreach (string r in Repairs) Log.Write($"repair: {r}");
        if (Repairs.Count > 0) SaveNow();
    }

    // ------------------------------------------------------------------

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "P02");

    public static string Path_ => System.IO.Path.Combine(Dir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path_), Opts)
                             ?? new AppConfig();
                loaded.Repair();
                return loaded;
            }
        }
        catch (Exception ex)
        {
            // A corrupt config must not stop the app from starting.
            Log.Write($"config load failed: {ex.Message}");
        }
        return new AppConfig();
    }

    private readonly object _saveGate = new();
    private System.Threading.Timer? _debounce;

    /// <summary>
    /// Queues a save. Dragging a slider raises a change per tick, and writing
    /// the file on each one is both pointless and a chance to be interrupted
    /// mid-write.
    /// </summary>
    public void Save()
    {
        lock (_saveGate)
        {
            _debounce ??= new System.Threading.Timer(_ => SaveNow());
            _debounce.Change(400, Timeout.Infinite);
        }
    }

    /// <summary>Writes immediately. Called on exit so nothing pending is lost.</summary>
    public void SaveNow()
    {
        try
        {
            lock (_saveGate)
            {
                Directory.CreateDirectory(Dir);
                // Write beside the real file and swap, so a crash mid-write
                // cannot leave a half-written config behind.
                string tmp = Path_ + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
                File.Move(tmp, Path_, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"config save failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Logging that never blocks the caller.
///
/// This used to create the directory and open, append and close the file on
/// every single line, under a lock, on the monitor thread - during firing that
/// is file I/O in the middle of the loop that is supposed to be watching your
/// health. Lines are queued and written by a background thread instead.
/// </summary>
internal static class Log
{
    private static readonly System.Collections.Concurrent.BlockingCollection<string> Queue
        = new(new System.Collections.Concurrent.ConcurrentQueue<string>(), 8192);

    public static string Path_ => System.IO.Path.Combine(AppConfig.Dir, "p02.log");

    static Log()
    {
        var t = new Thread(Pump) { IsBackground = true, Name = "P02 log" };
        t.Start();
    }

    public static void Write(string msg)
    {
        // Dropping a line is better than stalling the poll loop behind a disk.
        Queue.TryAdd($"{DateTime.Now:HH:mm:ss.fff}  {msg}");
    }

    private static void Pump()
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            Rotate();
        }
        catch { /* carry on; writes below will fail quietly */ }

        foreach (string first in Queue.GetConsumingEnumerable())
        {
            try
            {
                using var w = new StreamWriter(Path_, append: true);
                w.WriteLine(first);
                // Drain whatever else has piled up in the same open file.
                while (Queue.TryTake(out string? more)) w.WriteLine(more);
            }
            catch { /* a log must never take the app down */ }
        }
    }

    /// <summary>Keeps the file from growing without bound across sessions.</summary>
    private static void Rotate()
    {
        var f = new FileInfo(Path_);
        if (!f.Exists || f.Length < 5_000_000) return;
        string old = Path_ + ".1";
        if (File.Exists(old)) File.Delete(old);
        File.Move(Path_, old);
    }
}
