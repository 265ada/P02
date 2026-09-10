using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace P02;

/// <summary>
/// Settings as a short block of text, so one setup can be handed to another.
///
/// Only the settings that describe how it behaves travel. Where things are on
/// a particular screen does not: two machines rarely share a resolution, and a
/// region copied from someone else's screen is worse than none at all - it
/// points confidently at nothing. Nor do a character's own maxima, which read
/// themselves back in seconds.
///
/// Nor, now, do keys. A shared block used to arrive and quietly rebind the
/// flask keys of whoever loaded it, which is both a surprise and a hazard: the
/// keys are the one setting where being wrong means pressing something else
/// mid-fight. Everyone keeps their own.
///
/// It carries the version it came from and refuses to load into a different
/// one. Settings gain meanings between releases - a hold time that was a rate
/// limit, a maximum that was a rule and is now a reading - so a block written
/// by one version cannot be trusted to mean the same thing in another.
/// </summary>
internal static class SettingsShare
{
    private const string Marker = "P02";

    /// <summary>
    /// What actually travels.
    ///
    /// A whitelist rather than a list of exclusions, because the old block was
    /// the entire configuration file with a handful of keys removed - thousands
    /// of characters of screen geometry, colour calibration and per-character
    /// numbers, none of which mean anything on another machine, all of it
    /// pasted into chat windows that cut it off halfway.
    /// </summary>
    private static readonly string[] Shared =
    [
        "pollHz", "combatGraceMs", "useMemory", "numbersOnly", "inputMethod",
        "soundOnFire", "soundGapMs", "soundGainDb", "soundWhenDisarmed",
        "overlayOn", "overlaySnap", "overlayAutoHide", "overlayShowMana",
        "overlayFollowBar", "overlayLocked", "overlayClickThrough", "slotAuto",
        "checkUpdatesOnStart", "autoInstall", "autoInstallMaxBehind",
        "resumeArmed", "hideFromCapture", "startMinimised", "warnAtLaunch",
    ];

    /// <summary>Per-pool tunables. Keys are deliberately absent.</summary>
    private static readonly string[] SharedPool =
    [
        "enabled", "useText", "threshold", "panicBelow", "uberBelow",
        "lastDitchBelow", "cooldownMs", "panicCooldownMs", "holdMs",
        "burstCount", "burstGapMs", "confirmFrames", "ignoreBelow",
        "blindGraceMs", "actOnStaleMs", "requireTextMs", "verifyEffect",
        "verifyWindowMs", "fastDropPctPerSec", "noEffectBackoffMs",
        "noEffectBefore",
    ];

    private static readonly string[] Pools = ["life", "mana", "shield"];

    public static string Export(AppConfig cfg, string version)
    {
        var all = JsonNode.Parse(JsonSerializer.Serialize(cfg, Options))!.AsObject();

        // Positional, not named.
        //
        // Named, the same block came to 680 characters, and almost all of that
        // was the names: "fastDropPctPerSec" costs more than the number it
        // introduces, three times over. Both sides already refuse to talk
        // across versions, so both sides already agree on the order, and the
        // names are pure overhead on a thing meant to be pasted into chat.
        var flat = new JsonArray();

        foreach (string key in Shared)
            flat.Add(all[key]?.DeepClone());

        foreach (string pool in Pools)
        {
            var from = all[pool] as JsonObject;
            foreach (string key in SharedPool)
                flat.Add(from?[key]?.DeepClone());
        }

        return $"{Marker} {version} {Pack(flat.ToJsonString(Compact))}";
    }

    /// <summary>
    /// Applies a block onto an existing config, leaving everything local alone.
    /// Returns null on success, or why it was refused.
    /// </summary>
    public static string? Import(string text, AppConfig into, string version)
    {
        var parts = text.Trim().Split((char[])[' ', '\n', '\r', '\t'],
                                      StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != Marker)
            return "That does not look like exported P02 settings - it should start with "
                   + $"\"{Marker}\" and be one line long.";

        string from = parts[1].TrimStart('v', 'V');
        if (from != version)
            return $"Those settings came from v{from} and this is v{version}. Settings gain "
                   + "meanings between releases, so a block from another version cannot be "
                   + "trusted to mean the same thing here. Update both sides to match.";

        JsonArray incoming;
        try
        {
            incoming = JsonNode.Parse(Unpack(parts[2]))?.AsArray()
                       ?? throw new JsonException("empty");
        }
        catch (Exception ex)
        {
            return $"That text is not readable as settings: {ex.Message}";
        }

        if (incoming.Count != Shared.Length + Pools.Length * SharedPool.Length)
            return "Those settings are the right version but the wrong shape - the block "
                   + "looks truncated. Copy the whole line.";

        var mine = JsonNode.Parse(JsonSerializer.Serialize(into, Options))!.AsObject();

        int at = 0;
        foreach (string key in Shared)
        {
            if (incoming[at] is { } v) mine[key] = v.DeepClone();
            at++;
        }

        foreach (string pool in Pools)
        {
            var target = mine[pool] as JsonObject;
            foreach (string key in SharedPool)
            {
                if (target is not null && incoming[at] is { } v) target[key] = v.DeepClone();
                at++;
            }
        }

        var merged = JsonSerializer.Deserialize<AppConfig>(mine.ToJsonString(Options), Options);
        if (merged is null) return "Those settings could not be applied.";

        into.CopyFrom(merged);
        return null;
    }

    /// <summary>
    /// Squeezed and base64'd, because this is meant to be pasted into a chat
    /// message rather than attached to one.
    /// </summary>
    private static string Pack(string json)
    {
        using var into = new MemoryStream();
        using (var zip = new DeflateStream(into, CompressionLevel.SmallestSize, true))
        {
            byte[] raw = Encoding.UTF8.GetBytes(json);
            zip.Write(raw, 0, raw.Length);
        }
        return Convert.ToBase64String(into.ToArray()).TrimEnd('=')
                      .Replace('+', '-').Replace('/', '_');
    }

    private static string Unpack(string packed)
    {
        string b64 = packed.Replace('-', '+').Replace('_', '/');
        b64 += new string('=', (4 - b64.Length % 4) % 4);

        using var from = new MemoryStream(Convert.FromBase64String(b64));
        using var zip = new DeflateStream(from, CompressionMode.Decompress);
        using var text = new StreamReader(zip, Encoding.UTF8);
        return text.ReadToEnd();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
