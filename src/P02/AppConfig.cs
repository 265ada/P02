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
    public int HoldMs { get; set; } = 30;

    /// <summary>Normal gap between presses while sitting below the trigger.</summary>
    public int CooldownMs { get; set; } = 900;

    /// <summary>Consecutive low reads required, so one odd frame can't fire.</summary>
    public int ConfirmFrames { get; set; } = 2;

    // --- emergency response ---------------------------------------------

    /// <summary>Below this fraction, switch to the short gap and fire on sight.</summary>
    public double PanicBelow { get; set; } = 0.30;

    /// <summary>Gap between presses while panicking. Charges allow this.</summary>
    public int PanicCooldownMs { get; set; } = 260;

    /// <summary>Losing more than this many percent per second also counts as
    /// panic, even if you are still above PanicBelow. A big hit is caught on
    /// the way down instead of after it lands.</summary>
    public double FastDropPctPerSec { get; set; } = 30;

    /// <summary>Presses sent per trigger. Raise if one charge is not enough.</summary>
    public int BurstCount { get; set; } = 1;

    public int BurstGapMs { get; set; } = 70;

    // --- pixel classification -------------------------------------------
    /// <summary>"red" for life, "blue" for mana.</summary>
    public string Hue { get; set; } = "red";
    public double ChannelRatio { get; set; } = 1.35;
    public int MinValue { get; set; } = 50;
    public double BandFraction { get; set; } = 0.30;
    public double RowThreshold { get; set; } = 0.55;
    public int MinRun { get; set; } = 4;

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

    public int PollHz { get; set; } = 30;

    /// <summary>Only act while the focused window title contains this. Blank = any.</summary>
    public string WindowMatch { get; set; } = "Path of Exile";

    public string ArmHotkey { get; set; } = "F8";
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

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path_, JsonSerializer.Serialize(this, Opts));
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
