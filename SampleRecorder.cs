using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WowFisher;

internal sealed class SampleRecorder : IDisposable
{
    private const int FramesPerSecond = 24;
    private const int FrameLimit = FramesPerSecond * 12;
    private readonly string _path;
    private readonly MjpegAviWriter _video;
    private readonly long _startedAt;
    private readonly MemoryStream _jpeg = new();
    private readonly EncoderParameters _parameters = new(1);
    private readonly ImageCodecInfo _encoder;
    private readonly Size _videoSize;
    private Bitmap? _scaled;

    public SampleRecorder(string folder, Size frameSize, long now)
    {
        _startedAt = now;
        double scale = Math.Min(1.0, Math.Min(1280.0 / frameSize.Width, 720.0 / frameSize.Height));
        _videoSize = new Size((int)Math.Max(1, frameSize.Width * scale), (int)Math.Max(1, frameSize.Height * scale));
        _encoder = ImageCodecInfo.GetImageEncoders().First(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        _parameters.Param[0] = new EncoderParameter(Encoder.Quality, 90L);

        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, $"Bobber-{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}.avi");
        var file = new FileStream(_path + ".partial", FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            _video = new MjpegAviWriter(file, _videoSize, FramesPerSecond);
        }
        catch
        {
            file.Dispose();
            Dispose();
            throw;
        }
    }

    public bool IsFull => _video.FrameCount >= FrameLimit;

    public bool WantsFrame(long now) => _video.FrameCount < FramesAt(now);

    public void AddFrame(Bitmap frame, long now)
    {
        int expected = FramesAt(now);
        if (_video.FrameCount >= expected)
        {
            return;
        }

        // Preserve timing when screen grabs arrive later than the next video frame.
        while (_video.FrameCount + 1 < expected && _jpeg.Length > 0)
        {
            WriteJpeg();
        }

        _jpeg.SetLength(0);
        ScaleFrame(frame).Save(_jpeg, _encoder, _parameters);
        while (_video.FrameCount < expected)
        {
            WriteJpeg();
        }
    }

    public string? Save()
    {
        if (_video.FrameCount == 0)
        {
            return null;
        }

        _video.Finish();
        _video.Dispose();
        File.Move(_path + ".partial", _path);
        return _path;
    }

    public void Dispose()
    {
        _scaled?.Dispose();
        _parameters.Dispose();
        _jpeg.Dispose();
        try
        {
            _video?.Dispose();
            File.Delete(_path + ".partial");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private int FramesAt(long now) => (int)Math.Min(FrameLimit, 1 + (now - _startedAt) * FramesPerSecond / 1000);

    private void WriteJpeg() => _video.WriteFrame(_jpeg.GetBuffer().AsSpan(0, (int)_jpeg.Length));

    private Bitmap ScaleFrame(Bitmap frame)
    {
        if (frame.Size == _videoSize)
        {
            return frame;
        }

        _scaled ??= new Bitmap(_videoSize.Width, _videoSize.Height, PixelFormat.Format24bppRgb);
        using Graphics graphics = Graphics.FromImage(_scaled);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(frame, new Rectangle(Point.Empty, _videoSize));
        return _scaled;
    }
}
