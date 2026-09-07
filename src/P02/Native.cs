using System.Runtime.InteropServices;

namespace P02;

/// <summary>Win32 surface: screen capture, scancode input, window queries.</summary>
internal static class Native
{
    // ---- input -----------------------------------------------------------

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        // Sized to MOUSEINPUT, the largest member, so the struct matches Win32.
        [FieldOffset(0)] private readonly Padding _pad;

        [StructLayout(LayoutKind.Sequential, Size = 24)]
        private struct Padding { }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    // ---- windows ---------------------------------------------------------

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hWnd, char[] text, int count);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLengthW(nint hWnd);

    public static string ForegroundTitle()
    {
        nint h = GetForegroundWindow();
        if (h == 0) return string.Empty;
        int n = GetWindowTextLengthW(h);
        if (n <= 0) return string.Empty;
        var buf = new char[n + 1];
        int got = GetWindowTextW(h, buf, buf.Length);
        return new string(buf, 0, Math.Max(0, got));
    }
}
