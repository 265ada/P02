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

    // ---- timer resolution ------------------------------------------------

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    public static extern uint timeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    public static extern uint timeEndPeriod(uint uMilliseconds);

    // ---- gdi capture -----------------------------------------------------

    public const int SRCCOPY = 0x00CC0020;
    public const int CAPTUREBLT = 0x40000000;
    public const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public int bmiColors;
    }

    [DllImport("user32.dll")]
    public static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    public static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO bmi, uint usage,
                                               out nint ppvBits, nint hSection, uint offset);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int w, int h,
                                     nint hdcSrc, int xSrc, int ySrc, int rop);

    [DllImport("gdi32.dll")]
    public static extern bool GdiFlush();

    // ---- finding the game window ----------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT rect);

    public static string TitleOf(nint hWnd)
    {
        int n = GetWindowTextLengthW(hWnd);
        if (n <= 0) return string.Empty;
        var buf = new char[n + 1];
        int got = GetWindowTextW(hWnd, buf, buf.Length);
        return new string(buf, 0, Math.Max(0, got));
    }

    /// <summary>
    /// Screen rectangle of the first visible window whose title contains
    /// <paramref name="match"/>. Needed because the globes are in the corners
    /// of the *game*, and on a multi-monitor desktop the corners of the whole
    /// virtual screen are somewhere else entirely.
    /// </summary>
    public static Rectangle? FindWindowRect(string match)
    {
        if (string.IsNullOrWhiteSpace(match)) return null;
        Rectangle? best = null;

        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            if (!TitleOf(h).Contains(match, StringComparison.OrdinalIgnoreCase)) return true;
            if (!GetWindowRect(h, out RECT r)) return true;

            var rect = r.ToRectangle();
            // Skip tool windows and our own dialogs; the game is full screen.
            if (rect.Width < 400 || rect.Height < 300) return true;

            if (best is null || rect.Width * (long)rect.Height >
                                best.Value.Width * (long)best.Value.Height)
                best = rect;
            return true;
        }, 0);

        return best;
    }

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

    /// <summary>Handle of the first visible window whose title contains
    /// <paramref name="match"/>, or 0.</summary>
    public static nint FindWindowHandle(string match)
    {
        if (string.IsNullOrWhiteSpace(match)) return 0;
        nint best = 0;
        long bestArea = 0;

        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            if (!TitleOf(h).Contains(match, StringComparison.OrdinalIgnoreCase)) return true;
            if (!GetWindowRect(h, out RECT r)) return true;
            var rect = r.ToRectangle();
            if (rect.Width < 400 || rect.Height < 300) return true;
            long area = (long)rect.Width * rect.Height;
            if (area > bestArea) { bestArea = area; best = h; }
            return true;
        }, 0);

        return best;
    }

    // ---- keeping our own windows out of the capture -----------------------

    /// <summary>Window is skipped by screen-capture APIs entirely.</summary>
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    /// <summary>Normal: the window appears in captures like anything else.</summary>
    public const uint WDA_NONE = 0x00;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    /// <summary>
    /// Hides a window from BitBlt, so P02's own windows are not read as a globe
    /// when they sit over one.
    ///
    /// This is off by default and must stay that way: the flag hides the window
    /// from every capture path, not just ours - screenshots, the Snipping Tool,
    /// Discord and OBS all see nothing. Turning it on silently made the app
    /// impossible to screenshot or share.
    /// </summary>
    public static void ExcludeFromCapture(nint hWnd, bool hide)
    {
        try { SetWindowDisplayAffinity(hWnd, hide ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); }
        catch { /* older Windows: nothing to do */ }
    }

    // ---- posting keys to a window ----------------------------------------

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern uint MapVirtualKeyW(uint code, uint mapType);

    // --- per-pixel alpha windows ----------------------------------------

    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const byte AC_SRC_OVER = 0;
    public const byte AC_SRC_ALPHA = 1;
    public const int ULW_ALPHA = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT dst,
                                                  ref SIZE size, nint hdcSrc, ref POINT src,
                                                  int colorKey, ref BLENDFUNCTION blend,
                                                  int flags);
}
