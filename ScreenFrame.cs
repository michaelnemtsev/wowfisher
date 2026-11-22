using System.Drawing.Imaging;

namespace WowFisher;

internal sealed class ScreenFrame : IDisposable
{
    private Bitmap? _bitmap;
    private Graphics? _graphics;

    public Bitmap Read(Rectangle bounds)
    {
        if (_bitmap?.Size != bounds.Size)
        {
            Dispose();
            _bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            _graphics = Graphics.FromImage(_bitmap);
        }

        _graphics!.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return _bitmap;
    }

    public void Dispose()
    {
        _graphics?.Dispose();
        _bitmap?.Dispose();
        _graphics = null;
        _bitmap = null;
    }
}
