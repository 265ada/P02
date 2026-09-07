using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace P02;

/// <summary>
/// Grabs a fixed screen rectangle repeatedly.
///
/// Graphics.CopyFromScreen builds and tears down device contexts on every call,
/// which measured about 12 ms per grab — two globes alone capped the loop near
/// 45 Hz. This keeps the screen DC, the memory DC and a DIB section alive
/// between grabs, so a grab is one BitBlt plus a block copy.
///
/// Not thread-safe: one instance per watcher.
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    /// <summary>Bytes per pixel in <see cref="Buffer"/>; format is BGRA.</summary>
    public const int Bpp = 4;

    private nint _screenDc;
    private nint _memDc;
    private nint _dib;
    private nint _oldObj;
    private nint _bits;
    private Rectangle _rect;
    private byte[] _buffer = [];

    public int Width => _rect.Width;
    public int Height => _rect.Height;
    public byte[] Buffer => _buffer;

    private void Release()
    {
        if (_memDc != 0 && _oldObj != 0) Native.SelectObject(_memDc, _oldObj);
        if (_dib != 0) Native.DeleteObject(_dib);
        if (_memDc != 0) Native.DeleteDC(_memDc);
        if (_screenDc != 0) Native.ReleaseDC(0, _screenDc);
        _memDc = _dib = _screenDc = _oldObj = _bits = 0;
    }

    private bool Ensure(Rectangle r)
    {
        if (_dib != 0 && r.Size == _rect.Size)
        {
            _rect = r;
            return true;
        }
        Release();
        _rect = r;

        _screenDc = Native.GetDC(0);
        if (_screenDc == 0) return false;

        _memDc = Native.CreateCompatibleDC(_screenDc);
        if (_memDc == 0) { Release(); return false; }

        var bmi = new Native.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = r.Width;
        // Negative height gives a top-down bitmap, so row 0 is the top row and
        // the buffer needs no flipping.
        bmi.bmiHeader.biHeight = -r.Height;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;   // BI_RGB

        _dib = Native.CreateDIBSection(_screenDc, ref bmi, Native.DIB_RGB_COLORS,
                                       out _bits, 0, 0);
        if (_dib == 0 || _bits == 0) { Release(); return false; }

        _oldObj = Native.SelectObject(_memDc, _dib);
        _buffer = new byte[r.Width * r.Height * Bpp];
        return true;
    }

    /// <summary>Captures <paramref name="r"/> into <see cref="Buffer"/>.</summary>
    public bool Grab(Rectangle r)
    {
        if (r.Width < 1 || r.Height < 1) return false;
        try
        {
            if (!Ensure(r)) return false;

            if (!Native.BitBlt(_memDc, 0, 0, r.Width, r.Height,
                               _screenDc, r.Left, r.Top, Native.SRCCOPY))
            {
                // Happens while the desktop is locked or switching.
                return false;
            }

            Native.GdiFlush();
            Marshal.Copy(_bits, _buffer, 0, _buffer.Length);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"capture failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>A standalone snapshot, for the calibration preview.</summary>
    public static Bitmap Snapshot(Rectangle r)
    {
        var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height),
                             PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Copies a bitmap into a BGRA byte buffer for the detector.</summary>
    public static byte[] ToBuffer(Bitmap bmp)
    {
        var buf = new byte[bmp.Width * bmp.Height * Bpp];
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = bmp.Width * Bpp;
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, buf, y * rowBytes, rowBytes);
        }
        finally { bmp.UnlockBits(data); }
        return buf;
    }

    public void Dispose() => Release();
}
