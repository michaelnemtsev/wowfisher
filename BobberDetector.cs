using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WowFisher;

internal sealed class BobberDetector : IDisposable
{
    private const int AnalysisClientSide = 854;
    private const int TileSide = 8;
    private const int SplashSide = TileSide * 2;
    private const int WarmupFrames = 12;
    private const int MinimumSplashPixels = 52;
    private const int MaximumRestingChanges = 32;
    private const int RestingFrames = 8;
    private const int CooldownMilliseconds = 1200;

    private Bitmap? _sample;
    private Graphics? _graphics;
    private byte[] _pixels = [];
    private float[] _baseline = [];
    private int[] _tiles = [];
    private int _tileColumns;
    private int _tileRows;
    private int _warmup;
    private Point? _candidate;
    private Point? _lastSplash;
    private int _resting;
    private long _rearmAfter;

    public bool Analyze(Bitmap frame, Size clientSize, long now)
    {
        ReadPixels(frame, clientSize);
        if (_warmup < WarmupFrames)
        {
            UpdateBaseline(1f / ++_warmup);
            return false;
        }

        if (_lastSplash is { } lastSplash)
        {
            bool resting = now >= _rearmAfter && HasReturnedToRest(lastSplash);
            _resting = resting ? _resting + 1 : 0;
            if (_resting >= RestingFrames)
            {
                _lastSplash = null;
            }

            return false;
        }

        if (FindSplash() is not { } splash)
        {
            _candidate = null;
            UpdateBaseline(0.05f);
            return false;
        }

        // Confirm a second frame near the same position, allowing for an expanding splash.
        if (_candidate is not { } previous || Math.Abs(previous.X - splash.X) > TileSide ||
            Math.Abs(previous.Y - splash.Y) > TileSide)
        {
            _candidate = splash;
            return false;
        }

        _candidate = null;
        _lastSplash = splash;
        _resting = 0;
        _rearmAfter = now + CooldownMilliseconds;
        return true;
    }

    public void Reset()
    {
        _warmup = 0;
        _candidate = null;
        _lastSplash = null;
        _resting = 0;
    }

    public void Dispose()
    {
        _graphics?.Dispose();
        _sample?.Dispose();
    }

    private void ReadPixels(Bitmap frame, Size clientSize)
    {
        // Scale by the game window so tightening the selection preserves the splash size.
        float scale = Math.Min(1f, (float)AnalysisClientSide / Math.Max(clientSize.Width, clientSize.Height));
        var size = new Size(Math.Max(SplashSide, (int)MathF.Round(frame.Width * scale)),
            Math.Max(SplashSide, (int)MathF.Round(frame.Height * scale)));
        if (_sample?.Size != size)
        {
            Dispose();
            _sample = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            _graphics = Graphics.FromImage(_sample);
            _graphics.InterpolationMode = InterpolationMode.Bilinear;
            _pixels = new byte[size.Width * size.Height * 4];
            _baseline = new float[_pixels.Length];
            _tileColumns = (size.Width + TileSide - 1) / TileSide;
            _tileRows = (size.Height + TileSide - 1) / TileSide;
            _tiles = new int[_tileColumns * _tileRows];
            Reset();
        }

        var bounds = new Rectangle(Point.Empty, size);
        _graphics!.DrawImage(frame, bounds, new Rectangle(Point.Empty, frame.Size), GraphicsUnit.Pixel);
        BitmapData data = _sample.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            Marshal.Copy(data.Scan0, _pixels, 0, _pixels.Length);
        }
        finally
        {
            _sample.UnlockBits(data);
        }
    }

    private void UpdateBaseline(float weight)
    {
        for (int i = 0; i < _baseline.Length; i += 4)
        {
            _baseline[i] += (_pixels[i] - _baseline[i]) * weight;
            _baseline[i + 1] += (_pixels[i + 1] - _baseline[i + 1]) * weight;
            _baseline[i + 2] += (_pixels[i + 2] - _baseline[i + 2]) * weight;
        }
    }

    private Point? FindSplash()
    {
        int width = _sample!.Width;
        int height = _sample.Height;
        int totalBrightened = 0;
        int index = 0;
        Array.Clear(_tiles);

        for (int y = 0; y < height; y++)
        {
            int tileRow = y / TileSide * _tileColumns;
            for (int x = 0; x < width; x++, index += 4)
            {
                float blue = _pixels[index] - _baseline[index];
                float green = _pixels[index + 1] - _baseline[index + 1];
                float red = _pixels[index + 2] - _baseline[index + 2];
                if (blue >= 7 && green >= 7 && red >= 10 && red + 2 * green + blue >= 44)
                {
                    _tiles[tileRow + x / TileSide]++;
                    totalBrightened++;
                }
            }
        }

        int samplePixels = width * height;
        if (totalBrightened < MinimumSplashPixels || totalBrightened >= samplePixels * 3 / 4)
        {
            return null;
        }

        int peak = 0;
        var location = Point.Empty;
        for (int y = 0; y < _tileRows - 1; y++)
        {
            for (int x = 0; x < _tileColumns - 1; x++)
            {
                int tile = y * _tileColumns + x;
                int score = _tiles[tile] + _tiles[tile + 1] + _tiles[tile + _tileColumns] + _tiles[tile + _tileColumns + 1];
                if (score > peak)
                {
                    peak = score;
                    location = new Point(x * TileSide, y * TileSide);
                }
            }
        }

        Rectangle neighborhood = SplashBounds(location);
        int backgroundPixels = samplePixels - neighborhood.Width * neighborhood.Height;
        return peak >= MinimumSplashPixels && totalBrightened - peak <= Math.Max(32, backgroundPixels / 6)
            ? location : null;
    }

    private bool HasReturnedToRest(Point location)
    {
        Rectangle bounds = SplashBounds(location);
        int changedPixels = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                int index = (y * _sample!.Width + x) * 4;
                float blue = Math.Abs(_pixels[index] - _baseline[index]);
                float green = Math.Abs(_pixels[index + 1] - _baseline[index + 1]);
                float red = Math.Abs(_pixels[index + 2] - _baseline[index + 2]);
                if (red + 2 * green + blue >= 56 && ++changedPixels > MaximumRestingChanges)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private Rectangle SplashBounds(Point location) => Rectangle.Intersect(
        new Rectangle(location, new Size(SplashSide, SplashSide)), new Rectangle(Point.Empty, _sample!.Size));
}
