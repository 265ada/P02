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
    /// Readings at or below this are treated as "cannot see the globe" rather
    /// than "empty": a death screen, a loading screen, or something covering
    /// the corner. Pressing into those achieves nothing and burns charges.
    /// </summary>
    public double IgnoreBelow { get; set; } = 0.02;

    /// <summary>Below this fraction, switch to the short gap and fire on sight.</summary>
    public double PanicBelow { get; set; } = 0.30;

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

    /// <summary>Watch the globe after firing to see whether the press did anything.</summary>
    public bool VerifyEffect { get; set; } = true;

    /// <summary>How long to wait for the globe to start rising.</summary>
    public int VerifyWindowMs { get; set; } = 900;

    /// <summary>
    /// Hold off while a recovery that is already working is still running.
    /// Saves charges, but off by default: while something is hitting you hard,
    /// stacking another flask on top is usually the right call.
    /// </summary>
    public bool SkipWhileRecovering { get; set; }

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
        { Hue = "red", Key = "1", Threshold = 0.50 };

    public WatcherConfig Mana { get; set; } = new()
        { Hue = "blue", Key = "2", Threshold = 0.30, PanicBelow = 0.15 };

    /// <summary>Screen samples per second. 5-250; the loop reports what it
    /// actually achieved next to this in the UI.</summary>
    public int PollHz { get; set; } = 60;

    /// <summary>Only act while the focused window title contains this. Blank = any.</summary>
    public string WindowMatch { get; set; } = "Path of Exile";

    public string ArmHotkey { get; set; } = "F8";

    /// <summary>Short ding when a key is fired.</summary>
    public bool SoundOnFire { get; set; } = true;

    /// <summary>
    /// Minimum gap between dings. Firing can repeat several times a second, so
    /// this is what keeps it a signal rather than a machine gun.
    /// </summary>
    public int SoundGapMs { get; set; } = 6000;

    /// <summary>Boost over the original ding level, in dB. 0 is unchanged.</summary>
    public int SoundGainDb { get; set; }

    /// <summary>Where the window was last time, so it comes back as you left it.</summary>
    public int WindowX { get; set; } = -1;

    public int WindowY { get; set; } = -1;
    public bool StartMinimised { get; set; }
    public bool CheckUpdatesOnStart { get; set; } = true;

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
        if (SettingsVersion >= 4) return;

        if (HideFromCapture)
        {
            HideFromCapture = false;
            Repairs.Add("Turned off hiding from screen capture - it also hid the window from "
                        + "screenshots and screen sharing.");
        }

        foreach (var (name, w) in new[] { ("Life", Life), ("Mana", Mana) })
        {
            if (w.GlareIsLiquid)
            {
                w.GlareIsLiquid = false;
                Repairs.Add($"{name}: turned off counting bright pixels as liquid - the rune "
                            + "on the globe is near-white and was reading as full.");
            }

            // A margin this low accepts the drained globe, which is the same
            // hue as the liquid and only darker.
            if (w.ColourMargin < 10 && w.EmptyDominance < 0)
            {
                Repairs.Add($"{name}: colour margin was {w.ColourMargin}, low enough that an "
                            + "empty globe read as full. Reset to 30.");
                w.ColourMargin = 30;
            }

            if (w.HoldMs < 40)
            {
                Repairs.Add($"{name}: key was held for {w.HoldMs} ms, short enough for the "
                            + "game to miss it between frames. Raised to 70 ms.");
                w.HoldMs = 70;
            }
        }

        SettingsVersion = 4;
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
