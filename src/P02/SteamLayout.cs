using System.Text.RegularExpressions;

namespace P02;

/// <summary>
/// What Steam has bound the controller's back buttons to, read from Steam's
/// own layout file for the game.
///
/// L4, L5, R4 and R5 are on the controller and nowhere else. No virtual pad
/// has them - an Xbox pad and a PlayStation pad have no back buttons at all -
/// so they can never reach a game as themselves. Steam translates each one
/// into something the game understands, and that translation lives in a file
/// on this machine. Reading it means pressing "L4" presses exactly what L4
/// does in the game, and keeps doing so if you rebind it in Steam.
/// </summary>
internal static class SteamLayout
{
    /// <summary>Path of Exile 2 on Steam.</summary>
    private const string Game = "2694490";

    /// <summary>The back buttons: QytOCR's name, Steam's name, and how the list shows it.</summary>
    public static readonly (string Name, string Steam, string Says)[] Back =
    [
        ("L4", "button_back_left", "L4  -  back, lower left"),
        ("L5", "button_back_left_upper", "L5  -  back, upper left"),
        ("R4", "button_back_right", "R4  -  back, lower right"),
        ("R5", "button_back_right_upper", "R5  -  back, upper right"),
    ];

    public static bool IsBack(string name) =>
        Array.Exists(Back, b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? _file;
    private static DateTime _stamp;
    private static Dictionary<string, string> _bindings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The pad button a back button is bound to in Steam - "Left" for a back
    /// button that sends D-pad left - or null when it is not bound to a pad
    /// button at all. <paramref name="said"/> is Steam's own wording, for
    /// telling somebody what it found.
    /// </summary>
    public static string? Resolve(string back, out string said)
    {
        said = "";
        string? steamName = null;
        foreach (var b in Back)
            if (b.Name.Equals(back, StringComparison.OrdinalIgnoreCase)) steamName = b.Steam;
        if (steamName is null) return null;

        Refresh();
        if (!_bindings.TryGetValue(steamName, out string? binding))
        {
            said = _file is null ? "no Steam layout found for the game" : "not bound in Steam";
            return null;
        }

        said = binding;
        return ToPad(binding);
    }

    /// <summary>Re-reads the layout only when Steam has written a newer one.</summary>
    private static void Refresh()
    {
        try
        {
            string? file = FindFile();
            if (file is null) { _file = null; _bindings.Clear(); return; }

            var stamp = File.GetLastWriteTimeUtc(file);
            if (file == _file && stamp == _stamp) return;

            _bindings = Parse(File.ReadAllText(file));
            _file = file;
            _stamp = stamp;
            Log.Write($"controller: read Steam's layout for the game from {file}");
        }
        catch (Exception ex)
        {
            Log.Write($"controller: could not read Steam's layout - {ex.Message}");
        }
    }

    /// <summary>The newest layout Steam keeps for the game, for any account on this PC.</summary>
    private static string? FindFile()
    {
        string? steam = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";

        string root = Path.Combine(steam, "steamapps", "common", "Steam Controller Configs");
        if (!Directory.Exists(root)) return null;

        return Directory.EnumerateDirectories(root)
            .Select(account => Path.Combine(account, "config", Game))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "controller_*.vdf"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Each back button's first binding, from the text of a Steam layout.
    /// Only its own block is looked in, by matching braces, so a button left
    /// unbound cannot borrow the binding of the one after it.
    /// </summary>
    internal static Dictionary<string, string> Parse(string vdf)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, steamName, _) in Back)
        {
            var at = Regex.Match(vdf, "\"" + Regex.Escape(steamName) + "\"\\s*\\{");
            if (!at.Success) continue;

            int open = at.Index + at.Length - 1, depth = 0, end = -1;
            for (int i = open; i < vdf.Length; i++)
            {
                if (vdf[i] == '{') depth++;
                else if (vdf[i] == '}' && --depth == 0) { end = i; break; }
            }
            if (end < 0) continue;

            var binding = Regex.Match(vdf.Substring(open, end - open),
                                      "\"binding\"\\s*\"([^\"]*)\"");
            if (binding.Success) found[steamName] = binding.Groups[1].Value.Trim();
        }

        return found;
    }

    /// <summary>
    /// Steam's wording for a pad button, as QytOCR names it: "xinput_button
    /// DPAD_LEFT, , " is "Left". Anything that is not a pad button - a key, a
    /// mouse click - is null, because it is not something a pad can press.
    /// </summary>
    internal static string? ToPad(string binding)
    {
        var m = Regex.Match(binding, @"^\s*xinput_button\s+([A-Za-z_]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        return m.Groups[1].Value.ToUpperInvariant() switch
        {
            "A" => "A",
            "B" => "B",
            "X" => "X",
            "Y" => "Y",
            "SHOULDER_LEFT" => "LB",
            "SHOULDER_RIGHT" => "RB",
            "TRIGGER_LEFT" => "LT",
            "TRIGGER_RIGHT" => "RT",
            "DPAD_UP" => "Up",
            "DPAD_DOWN" => "Down",
            "DPAD_LEFT" => "Left",
            "DPAD_RIGHT" => "Right",
            "JOYSTICK_LEFT" => "LS",
            "JOYSTICK_RIGHT" => "RS",
            _ => null,
        };
    }
}
