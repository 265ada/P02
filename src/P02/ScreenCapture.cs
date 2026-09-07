using System.Drawing.Imaging;

namespace P02;

/// <summary>
/// Grabs a fixed screen rectangle repeatedly, reusing one bitmap so the poll
/// loop does not allocate. Not thread-safe: one instance per watcher thread.
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    private Bitmap? _bmp;
    private Graphics? _gfx;
    private Rectangle _rect;
    private byte[] _buffer = [];

    public int Width => _rect.Width;
    public int Height => _rect.Height;

    /// <summary>Bytes per pixel in <see cref="Buffer"/>; format is BGRA.</summary>
    public const int Bpp = 4;

    public byte[] Buffer => _buffer;

    private void Ensure(Rectangle r)
    {
        if (_bmp is not null && r == _rect) return;
        _gfx?.Dispose();
        _bmp?.Dispose();
        _rect = r;
        _bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        _gfx = Graphics.FromImage(_bmp);
        _buffer = new byte[r.Width * r.Height * Bpp];
    }

    /// <summary>Captures <paramref name="r"/> into <see cref="Buffer"/>.</summary>
    public bool Grab(Rectangle r)
    {
        if (r.Width < 1 || r.Height < 1) return false;
        try
        {
            Ensure(r);
            _gfx!.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);

            var data = _bmp!.LockBits(new Rectangle(0, 0, r.Width, r.Height),
                                      ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = r.Width * Bpp;
                for (int y = 0; y < r.Height; y++)
                {
                    nint src = data.Scan0 + y * data.Stride;
                    System.Runtime.InteropServices.Marshal.Copy(
                        src, _buffer, y * rowBytes, rowBytes);
                }
            }
            finally
            {
                _bmp.UnlockBits(data);
            }
            return true;
        }
        catch (Exception ex)
        {
            // CopyFromScreen throws while the desktop is locked or switching.
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
                System.Runtime.InteropServices.Marshal.Copy(
                    data.Scan0 + y * data.Stride, buf, y * rowBytes, rowBytes);
        }
        finally { bmp.UnlockBits(data); }
        return buf;
    }

    public void Dispose()
    {
        _gfx?.Dispose();
        _bmp?.Dispose();
    }
}
