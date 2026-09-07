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
    public int HoldMs { get; set; } = 20;

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

    /// <summary>Count near-white pixels as liquid. The globes carry a specular
    /// highlight that is not blue or red at all, and it would otherwise punch a
    /// hole in the middle of the mask.</summary>
    public bool GlareIsLiquid { get; set; } = true;
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

    /// <summary>Where the window was last time, so it comes back as you left it.</summary>
    public int WindowX { get; set; } = -1;

    public int WindowY { get; set; } = -1;
    public bool StartMinimised { get; set; }
    public bool CheckUpdatesOnStart { get; set; } = true;

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
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Path_), Opts)
                       ?? new AppConfig();
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

internal static class Log
{
    private static readonly object Gate = new();

    public static string Path_ => System.IO.Path.Combine(AppConfig.Dir, "p02.log");

    public static void Write(string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppConfig.Dir);
                File.AppendAllText(Path_, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}");
            }
        }
        catch { /* logging must never throw into the poll loop */ }
    }
}
