using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace P02;

/// <summary>
/// Settings as a block of text, so one setup can be handed to another.
///
/// Everything that describes how it behaves travels; everything that describes
/// where things are on a particular screen does not. Two machines rarely share
/// a resolution or a monitor layout, and a region copied from someone else's
/// screen is worse than no region at all - it points confidently at nothing.
/// The same goes for a character's own maxima, which are read back in seconds
/// anyway.
///
/// It carries the version it came from and refuses to load into a different
/// one. Settings gain meanings between releases - a hold time that was a rate
/// limit, a maximum that was a rule and is now a reading - so a block written
/// by one version cannot be trusted to mean the same thing in another.
/// </summary>
internal static class SettingsShare
{
    private const string Marker = "P02-SETTINGS";

    /// <summary>Screen geography and per-character values, which do not travel.</summary>
    private static readonly string[] Local =
    [
        "region", "textRegion", "fullRow", "emptyRow", "knownMax",
        "overlayX", "overlayY", "windowX", "windowY", "repairs",
    ];

    public static string Export(AppConfig cfg, string version)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(cfg, Options))!.AsObject();
        Strip(node);

        var text = new StringBuilder();
        text.AppendLine($"{Marker} v{version}");
        text.AppendLine("# Everything except the screen regions and your own maxima.");
        text.AppendLine("# Those are found again on the machine this is loaded into.");
        text.AppendLine(node.ToJsonString(Options));
        return text.ToString();
    }

    /// <summary>
    /// Applies a block onto an existing config, leaving the local parts alone.
    /// Returns null on success, or why it was refused.
    /// </summary>
    public static string? Import(string text, AppConfig into, string version)
    {
        string[] lines = text.Split((char)10);
        if (lines.Length == 0 || !lines[0].TrimStart().StartsWith(Marker, StringComparison.Ordinal))
            return "That does not look like exported P02 settings - the first line should "
                   + $"say {Marker}.";

        string from = lines[0].Trim()[Marker.Length..].Trim().TrimStart('v', 'V');
        if (from != version)
            return $"Those settings came from v{from} and this is v{version}. Settings gain "
                   + "meanings between releases, so a block from another version cannot be "
                   + "trusted to mean the same thing here. Update both sides to match.";

        int start = text.IndexOf('{');
        if (start < 0) return "There is no settings block in that text.";

        JsonObject incoming;
        try
        {
            incoming = JsonNode.Parse(text[start..])?.AsObject()
                       ?? throw new JsonException("empty");
        }
        catch (Exception ex)
        {
            return $"That text is not readable as settings: {ex.Message}";
        }

        Strip(incoming);

        // Merge onto what is here, so the regions and maxima already set up on
        // this machine survive untouched.
        var mine = JsonNode.Parse(JsonSerializer.Serialize(into, Options))!.AsObject();
        Merge(mine, incoming);

        var merged = JsonSerializer.Deserialize<AppConfig>(mine.ToJsonString(Options), Options);
        if (merged is null) return "Those settings could not be applied.";

        into.CopyFrom(merged);
        return null;
    }

    private static void Strip(JsonObject node)
    {
        foreach (string key in Local) node.Remove(key);

        foreach (var pair in node.ToArray())
            if (pair.Value is JsonObject child)
                Strip(child);
    }

    private static void Merge(JsonObject into, JsonObject from)
    {
        foreach (var pair in from)
        {
            if (pair.Value is JsonObject sub && into[pair.Key] is JsonObject mine)
            {
                Merge(mine, sub);
                continue;
            }

            into[pair.Key] = pair.Value?.DeepClone();
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
