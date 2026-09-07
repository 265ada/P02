namespace P02;

/// <summary>
/// Sends keys as hardware scancodes. Games read raw scancodes, so the usual
/// virtual-key helpers get ignored; this is the form that actually registers.
/// </summary>
internal static class KeySender
{
    private static readonly Dictionary<string, ushort> Scan = new(StringComparer.OrdinalIgnoreCase)
    {
        ["escape"] = 0x01,
        ["1"] = 0x02, ["2"] = 0x03, ["3"] = 0x04, ["4"] = 0x05, ["5"] = 0x06,
        ["6"] = 0x07, ["7"] = 0x08, ["8"] = 0x09, ["9"] = 0x0A, ["0"] = 0x0B,
        ["-"] = 0x0C, ["="] = 0x0D, ["backspace"] = 0x0E, ["tab"] = 0x0F,
        ["q"] = 0x10, ["w"] = 0x11, ["e"] = 0x12, ["r"] = 0x13, ["t"] = 0x14,
        ["y"] = 0x15, ["u"] = 0x16, ["i"] = 0x17, ["o"] = 0x18, ["p"] = 0x19,
        ["["] = 0x1A, ["]"] = 0x1B, ["enter"] = 0x1C, ["lctrl"] = 0x1D,
        ["a"] = 0x1E, ["s"] = 0x1F, ["d"] = 0x20, ["f"] = 0x21, ["g"] = 0x22,
        ["h"] = 0x23, ["j"] = 0x24, ["k"] = 0x25, ["l"] = 0x26, [";"] = 0x27,
        ["'"] = 0x28, ["`"] = 0x29, ["lshift"] = 0x2A, ["\\"] = 0x2B,
        ["z"] = 0x2C, ["x"] = 0x2D, ["c"] = 0x2E, ["v"] = 0x2F, ["b"] = 0x30,
        ["n"] = 0x31, ["m"] = 0x32, [","] = 0x33, ["."] = 0x34, ["/"] = 0x35,
        ["rshift"] = 0x36, ["lalt"] = 0x38, ["space"] = 0x39,
        ["f1"] = 0x3B, ["f2"] = 0x3C, ["f3"] = 0x3D, ["f4"] = 0x3E,
        ["f5"] = 0x3F, ["f6"] = 0x40, ["f7"] = 0x41, ["f8"] = 0x42,
        ["f9"] = 0x43, ["f10"] = 0x44, ["f11"] = 0x57, ["f12"] = 0x58,
        // numpad. Same scancodes as the navigation keys below but NOT flagged
        // extended, which is exactly what separates numpad 0 from Insert. Flask
        // binds live here often enough to matter.
        ["numpad0"] = 0x52, ["numpad1"] = 0x4F, ["numpad2"] = 0x50,
        ["numpad3"] = 0x51, ["numpad4"] = 0x4B, ["numpad5"] = 0x4C,
        ["numpad6"] = 0x4D, ["numpad7"] = 0x47, ["numpad8"] = 0x48,
        ["numpad9"] = 0x49, ["numpad."] = 0x53, ["numpad*"] = 0x37,
        ["numpad-"] = 0x4A, ["numpad+"] = 0x4E,

        // extended (0xE0-prefixed) keys
        ["rctrl"] = 0x1D, ["ralt"] = 0x38, ["insert"] = 0x52, ["delete"] = 0x53,
        ["home"] = 0x47, ["end"] = 0x4F, ["pageup"] = 0x49, ["pagedown"] = 0x51,
        ["up"] = 0x48, ["down"] = 0x50, ["left"] = 0x4B, ["right"] = 0x4D,
        ["numpad/"] = 0x35,
    };

    private static readonly HashSet<string> Extended = new(StringComparer.OrdinalIgnoreCase)
    {
        "rctrl", "ralt", "insert", "delete", "home", "end",
        "pageup", "pagedown", "up", "down", "left", "right", "numpad/",
    };

    public static IEnumerable<string> KeyNames => Scan.Keys.OrderBy(k => k);

    public static bool IsKnown(string key) => Scan.ContainsKey(key.Trim());

    private static Native.INPUT Make(ushort scan, bool up, bool extended)
    {
        uint flags = Native.KEYEVENTF_SCANCODE | (up ? Native.KEYEVENTF_KEYUP : 0);
        if (extended) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        var i = new Native.INPUT { type = Native.INPUT_KEYBOARD };
        i.u.ki = new Native.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags };
        return i;
    }

    /// <summary>Taps <paramref name="key"/>. Blocks for <paramref name="holdMs"/>.</summary>
    public static void Tap(string key, int holdMs)
    {
        key = key.Trim();
        if (!Scan.TryGetValue(key, out ushort scan))
            throw new ArgumentException($"unknown key '{key}'");
        bool ext = Extended.Contains(key);
        int size = System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>();

        Native.SendInput(1, [Make(scan, false, ext)], size);
        Thread.Sleep(Math.Clamp(holdMs, 1, 500));
        Native.SendInput(1, [Make(scan, true, ext)], size);
    }

    /// <summary>Maps a WinForms key press back to a scancode name, for the
    /// click-to-bind boxes in the UI. Returns null for keys we cannot send.</summary>
    public static string? FromKeys(System.Windows.Forms.Keys k)
    {
        k &= System.Windows.Forms.Keys.KeyCode;
        string? name = k switch
        {
            >= System.Windows.Forms.Keys.D0 and <= System.Windows.Forms.Keys.D9
                => ((char)('0' + (k - System.Windows.Forms.Keys.D0))).ToString(),
            >= System.Windows.Forms.Keys.A and <= System.Windows.Forms.Keys.Z
                => k.ToString().ToLowerInvariant(),
            >= System.Windows.Forms.Keys.F1 and <= System.Windows.Forms.Keys.F12
                => k.ToString().ToLowerInvariant(),
            System.Windows.Forms.Keys.Space => "space",
            System.Windows.Forms.Keys.Enter => "enter",
            System.Windows.Forms.Keys.Tab => "tab",
            System.Windows.Forms.Keys.OemMinus => "-",
            System.Windows.Forms.Keys.Oemplus => "=",
            System.Windows.Forms.Keys.OemOpenBrackets => "[",
            System.Windows.Forms.Keys.OemCloseBrackets => "]",
            System.Windows.Forms.Keys.OemSemicolon => ";",
            System.Windows.Forms.Keys.OemQuotes => "'",
            System.Windows.Forms.Keys.Oemcomma => ",",
            System.Windows.Forms.Keys.OemPeriod => ".",
            System.Windows.Forms.Keys.OemQuestion => "/",
            System.Windows.Forms.Keys.OemPipe => "\\",
            System.Windows.Forms.Keys.Oemtilde => "`",
            >= System.Windows.Forms.Keys.NumPad0 and <= System.Windows.Forms.Keys.NumPad9
                => "numpad" + (int)(k - System.Windows.Forms.Keys.NumPad0),
            System.Windows.Forms.Keys.Decimal => "numpad.",
            System.Windows.Forms.Keys.Multiply => "numpad*",
            System.Windows.Forms.Keys.Subtract => "numpad-",
            System.Windows.Forms.Keys.Add => "numpad+",
            System.Windows.Forms.Keys.Divide => "numpad/",
            System.Windows.Forms.Keys.Insert => "insert",
            System.Windows.Forms.Keys.Delete => "delete",
            System.Windows.Forms.Keys.Home => "home",
            System.Windows.Forms.Keys.End => "end",
            System.Windows.Forms.Keys.PageUp => "pageup",
            System.Windows.Forms.Keys.PageDown => "pagedown",
            System.Windows.Forms.Keys.Up => "up",
            System.Windows.Forms.Keys.Down => "down",
            System.Windows.Forms.Keys.Left => "left",
            System.Windows.Forms.Keys.Right => "right",
            _ => null,
        };
        return name is not null && Scan.ContainsKey(name) ? name : null;
    }
}
