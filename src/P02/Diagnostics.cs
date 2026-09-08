using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace P02;

/// <summary>
/// Collects everything needed to explain a misbehaving setup into one zip,
/// plus a plain-text report that is safe to paste into a chat.
///
/// The things that actually turned out to matter, in order: which monitor the
/// game is on, whether the globe regions read plausible values, whether the
/// configured key resolves to the scancode you think it does, and what the log
/// says happened. All of that is here.
/// </summary>
internal static partial class Diagnostics
{
    // Anything that looks like a GitHub token never leaves the machine.
    [GeneratedRegex(@"gh[pousr]_[A-Za-z0-9]{16,}|github_pat_[A-Za-z0-9_]{20,}")]
    private static partial Regex SecretPattern();

    private static string Redact(string text) =>
        SecretPattern().Replace(text, "[REDACTED-TOKEN]");

    /// <summary>Builds the bundle. Returns the zip path.</summary>
    public static string Export(AppConfig cfg, MonitorEngine engine, Form owner)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string work = Path.Combine(Path.GetTempPath(), $"P02-diag-{stamp}");
        Directory.CreateDirectory(work);

        string report = BuildReport(cfg, engine, owner);
        File.WriteAllText(Path.Combine(work, "report.txt"), report);

        CaptureGlobe(cfg.Life, "life", work);
        CaptureGlobe(cfg.Mana, "mana", work);

        CopyTail(Log.Path_, Path.Combine(work, "p02-log-tail.txt"), 400);
        CopyTail(Path.Combine(AppConfig.Dir, "update.log"),
                 Path.Combine(work, "update-log.txt"), 200);

        // The config is included whole; it holds no secrets, and the token
        // lives in a separate file that is deliberately never collected.
        try
        {
            if (File.Exists(AppConfig.Path_))
                File.WriteAllText(Path.Combine(work, "config.json"),
                                  Redact(File.ReadAllText(AppConfig.Path_)));
        }
        catch (Exception ex) { Log.Write($"diag: config copy failed: {ex.Message}"); }

        string zip = Path.Combine(AppConfig.Dir, $"P02-diagnostics-{stamp}.zip");
        Directory.CreateDirectory(AppConfig.Dir);
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(work, zip, CompressionLevel.Optimal, false);

        // Leave the readable report beside the zip so it can be pasted without
        // unzipping anything.
        File.WriteAllText(Path.Combine(AppConfig.Dir, $"P02-diagnostics-{stamp}.txt"), report);

        try { Directory.Delete(work, true); } catch { /* best effort */ }
        return zip;
    }

    private static string BuildReport(AppConfig cfg, MonitorEngine engine, Form owner)
    {
        var b = new StringBuilder();
        void H(string title) => b.AppendLine().AppendLine($"== {title} ==");

        b.AppendLine($"P02 diagnostics  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        H("app");
        b.AppendLine($"version      : {Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "?"}");
        b.AppendLine($"exe          : {Environment.ProcessPath}");
        b.AppendLine($"elevated     : {IsElevated()}");
        b.AppendLine($"os           : {Environment.OSVersion.VersionString}");
        b.AppendLine($".net         : {Environment.Version}");
        b.AppendLine($"dpi          : {owner.DeviceDpi}");
        b.AppendLine($"armed        : {engine.Armed}");
        b.AppendLine($"poll wanted  : {cfg.PollHz}/s     actual: {engine.ActualHz}/s"
                     + $"     work per poll: {engine.LastPollMs:0.0} ms");
        b.AppendLine($"token file   : {TokenState()}");

        H("screens");
        foreach (var s in Screen.AllScreens)
            b.AppendLine($"{(s.Primary ? "*" : " ")} {s.DeviceName}  {s.Bounds}");
        b.AppendLine($"virtual      : {SystemInformation.VirtualScreen}");

        H("game window");
        b.AppendLine($"match string : \"{cfg.WindowMatch}\"");
        b.AppendLine($"focused now  : \"{engine.ForegroundTitle}\"");
        nint h = Native.FindWindowHandle(cfg.WindowMatch);
        if (h == 0)
        {
            b.AppendLine("found        : NO - no visible window matches that string");
        }
        else
        {
            Native.GetWindowRect(h, out var r);
            Native.GetWindowThreadProcessId(h, out uint pid);
            b.AppendLine($"found        : \"{Native.TitleOf(h)}\"");
            b.AppendLine($"bounds       : {r.ToRectangle()}");
            b.AppendLine($"pid          : {pid}");
            b.AppendLine($"process      : {ProcessNote((int)pid)}");
        }

        Globe(b, "life", cfg.Life);
        Globe(b, "mana", cfg.Mana);

        H("verdict");
        foreach (string line in Verdicts(cfg, engine, h)) b.AppendLine(line);

        return b.ToString();
    }

    private static void Globe(StringBuilder b, string name, WatcherConfig c)
    {
        b.AppendLine().AppendLine($"== {name} ==");
        b.AppendLine($"enabled      : {c.Enabled}");
        b.AppendLine($"region       : {c.Region}");
        b.AppendLine($"threshold    : {c.Threshold:P0}   panic below {c.PanicBelow:P0}");
        b.AppendLine($"cooldown     : {c.CooldownMs} ms   panic gap {c.PanicCooldownMs} ms");
        b.AppendLine($"burst        : {c.BurstCount} press(es), {c.BurstGapMs} ms apart, "
                     + $"hold {c.HoldMs} ms");
        b.AppendLine($"key          : \"{c.Key}\"  -> {KeyNote(c.Key)}");
        b.AppendLine($"colour       : margin {c.ColourMargin}, min brightness {c.MinValue}, "
                     + $"glare={c.GlareIsLiquid}");
        b.AppendLine($"verify       : {(c.VerifyEffect ? $"on, {c.VerifyWindowMs} ms window" : "off")}"
                     + $"   skip while recovering: {c.SkipWhileRecovering}");
        b.AppendLine($"calibration  : full row {c.FullRow}, empty row {c.EmptyRow}");
        b.AppendLine($"learned      : full dom/val {c.FullDominance}/{c.FullValue}, "
                     + $"empty dom/val {c.EmptyDominance}/{c.EmptyValue}");

        if (!c.Region.IsValid) { b.AppendLine("reading      : (no region set)"); return; }

        using var cap = new ScreenCapture();
        if (!cap.Grab(c.Region.ToRect())) { b.AppendLine("reading      : capture FAILED"); return; }

        int surface = OrbDetector.SurfaceRow(cap.Buffer, cap.Width, cap.Height, c);
        double frac = OrbDetector.FractionFromRow(surface, cap.Height, c);
        b.AppendLine($"reading      : {frac:P1}  (surface row {surface} of {cap.Height})");
    }

    private static IEnumerable<string> Verdicts(AppConfig cfg, MonitorEngine engine, nint gameWnd)
    {
        var notes = new List<string>();

        if (!cfg.Life.Enabled && !cfg.Mana.Enabled)
            notes.Add("- No globe is switched on, so nothing will ever fire.");

        if (gameWnd == 0 && !string.IsNullOrWhiteSpace(cfg.WindowMatch))
            notes.Add($"- No visible window contains \"{cfg.WindowMatch}\". Firing is gated on "
                      + "that, so nothing will fire until it matches. Note the test is a "
                      + "substring: \"Path of Exile\" does match \"Path of Exile 2\".");

        foreach (var (name, c) in new[] { ("life", cfg.Life), ("mana", cfg.Mana) })
        {
            if (!c.Enabled) continue;

            if (!c.Region.IsValid)
                notes.Add($"- {name}: no region set.");

            if (!KeySender.IsKnown(c.Key))
                notes.Add($"- {name}: key \"{c.Key}\" has no scancode and can never be sent.");

            if (c.FullRow < 0)
                notes.Add($"- {name}: never calibrated. A full globe will read low by however "
                          + "much frame the box caught. Press Full = 100%.");

            if (c.EmptyDominance < 0)
                notes.Add($"- {name}: Empty = 0% has not been done. If a drained globe still "
                          + "reads high, this is why.");

            if (c.ColourMargin <= 6)
                notes.Add($"- {name}: colour margin is {c.ColourMargin}, near the minimum. That "
                          + "usually means a drained globe counts as liquid and the reading "
                          + "never falls below the trigger.");

            if (c.HoldMs < 40)
                notes.Add($"- {name}: key held for only {c.HoldMs} ms. A game reads input once "
                          + "a frame, so below about 50 fps a press this short can go down and "
                          + "up between frames and never register. 70 ms is safer.");

            int burstMs = c.BurstCount * c.HoldMs + Math.Max(0, c.BurstCount - 1) * c.BurstGapMs;
            if (burstMs > c.PanicCooldownMs)
                notes.Add($"- {name}: a burst takes about {burstMs} ms to send but the panic gap "
                          + $"is {c.PanicCooldownMs} ms, so requests will be skipped. That is "
                          + "harmless, but the real rate is one burst per ~" + burstMs + " ms.");
        }

        if (cfg.PollHz < 30)
            notes.Add($"- Poll rate is {cfg.PollHz}/s, so the reading is only refreshed every "
                      + $"{1000 / Math.Max(1, cfg.PollHz)} ms and firing can be that late. 60 is "
                      + "a better starting point.");

        if (cfg.Life.Enabled && cfg.Mana.Enabled)
            notes.Add("- Both globes are on. Each costs a screen capture per poll (about 9 ms "
                      + "idle, closer to 20 ms with a game running), so switching one off "
                      + "roughly doubles the achievable rate.");

        if (engine.ActualHz > 0 && engine.ActualHz < cfg.PollHz - 5)
            notes.Add($"- Asking for {cfg.PollHz} polls/s but achieving {engine.ActualHz}. Screen "
                      + "capture costs about 9 ms per globe regardless of size, so roughly 60/s "
                      + "is the ceiling. Asking for more only spins a core.");

        if (IsElevated())
            notes.Add("- P02 is running elevated. That is fine, but unusual; it only matters if "
                      + "the game is elevated too.");

        if (notes.Count == 0) notes.Add("- Nothing obviously wrong in the configuration.");
        return notes;
    }

    private static string KeyNote(string key)
    {
        if (!KeySender.IsKnown(key)) return "UNKNOWN KEY - cannot be sent";
        return $"scancode 0x{KeySender.ScanOf(key):X2}"
             + (KeySender.IsExtended(key) ? " (extended)" : "");
    }

    private static string ProcessNote(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // Reading MainModule across an elevation boundary is denied, which
            // is a useful hint: an elevated game silently discards our input.
            return $"{p.ProcessName} ({p.MainModule?.FileName})";
        }
        catch (Exception ex)
        {
            return $"could not read process details ({ex.GetType().Name}). This often means the "
                 + "game runs elevated - if so, Windows discards our key presses unless P02 is "
                 + "elevated too.";
        }
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string TokenState()
    {
        string path = Path.Combine(AppConfig.Dir, "token.txt");
        if (!File.Exists(path)) return "absent";
        try
        {
            return File.ReadAllText(path).Trim().Length == 0 ? "present but EMPTY" : "present";
        }
        catch { return "unreadable"; }
    }

    private static void CaptureGlobe(WatcherConfig c, string name, string dir)
    {
        if (!c.Region.IsValid) return;
        try
        {
            using var shot = ScreenCapture.Snapshot(c.Region.ToRect());
            shot.Save(Path.Combine(dir, $"{name}.png"), System.Drawing.Imaging.ImageFormat.Png);
            using var mask = OrbDetector.MaskOverlay(shot, c);
            mask.Save(Path.Combine(dir, $"{name}-mask.png"),
                      System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) { Log.Write($"diag: {name} capture failed: {ex.Message}"); }
    }

    private static void CopyTail(string src, string dst, int lines)
    {
        try
        {
            if (!File.Exists(src)) return;
            var all = File.ReadLines(src).ToList();
            var tail = all.Count > lines ? all.Skip(all.Count - lines) : all;
            File.WriteAllLines(dst, tail.Select(Redact));
        }
        catch (Exception ex) { Log.Write($"diag: tail of {src} failed: {ex.Message}"); }
    }
}
