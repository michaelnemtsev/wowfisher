namespace WowFisher;

internal sealed class MjpegAviWriter : IDisposable
{
    private readonly BinaryWriter _writer;
    private readonly int _framesPerSecond;
    private readonly List<(int Offset, int Size)> _index = [];
    private readonly long _fileSize;
    private readonly long _frameCount;
    private readonly long _streamLength;
    private readonly long _bytesPerSecond;
    private readonly long _bufferSize;
    private readonly long _streamBufferSize;
    private readonly long _movieSize;
    private readonly long _movieOrigin;
    private int _largestFrame;
    private bool _disposed;

    public MjpegAviWriter(Stream stream, Size size, int framesPerSecond)
    {
        _writer = new BinaryWriter(stream);
        _framesPerSecond = framesPerSecond;
        _fileSize = BeginChunk("RIFF"u8, "AVI "u8);
        long header = BeginChunk("LIST"u8, "hdrl"u8);

        long mainHeader = BeginChunk("avih"u8);
        _writer.Write(1_000_000 / framesPerSecond);
        _bytesPerSecond = ReserveInt32();
        _writer.Write(0); // Padding granularity
        _writer.Write(0x10); // AVIF_HASINDEX
        _frameCount = ReserveInt32();
        _writer.Write(0); // Initial frames
        _writer.Write(1); // Streams
        _bufferSize = ReserveInt32();
        _writer.Write(size.Width);
        _writer.Write(size.Height);
        _writer.Write(new byte[16]); // Reserved
        EndChunk(mainHeader);

        long streamList = BeginChunk("LIST"u8, "strl"u8);
        long streamHeader = BeginChunk("strh"u8);
        _writer.Write("vidsMJPG"u8);
        _writer.Write(0); // Flags
        _writer.Write(0); // Priority and language
        _writer.Write(0); // Initial frames
        _writer.Write(1); // Scale
        _writer.Write(framesPerSecond); // Rate
        _writer.Write(0); // Start
        _streamLength = ReserveInt32();
        _streamBufferSize = ReserveInt32();
        _writer.Write(-1); // Default quality
        _writer.Write(0); // Variable sample size
        _writer.Write((short)0);
        _writer.Write((short)0);
        _writer.Write((short)size.Width);
        _writer.Write((short)size.Height);
        EndChunk(streamHeader);

        long format = BeginChunk("strf"u8);
        _writer.Write(40); // BITMAPINFOHEADER size
        _writer.Write(size.Width);
        _writer.Write(size.Height);
        _writer.Write((short)1); // Planes
        _writer.Write((short)24); // Bits per pixel
        _writer.Write("MJPG"u8);
        _writer.Write(((size.Width * 3 + 3) & ~3) * size.Height);
        _writer.Write(new byte[16]); // Resolution and palette fields
        EndChunk(format);
        EndChunk(streamList);
        EndChunk(header);

        _movieSize = BeginChunk("LIST"u8, "movi"u8);
        _movieOrigin = _movieSize + sizeof(int);
    }

    public int FrameCount => _index.Count;

    public void WriteFrame(ReadOnlySpan<byte> jpeg)
    {
        int offset = checked((int)(_writer.BaseStream.Position - _movieOrigin));
        _writer.Write("00dc"u8);
        _writer.Write(jpeg.Length);
        _writer.Write(jpeg);
        if ((jpeg.Length & 1) != 0)
        {
            _writer.Write((byte)0);
        }

        _index.Add((offset, jpeg.Length));
        _largestFrame = Math.Max(_largestFrame, jpeg.Length);
    }

    public void Finish()
    {
        EndChunk(_movieSize);
        long index = BeginChunk("idx1"u8);
        foreach ((int offset, int size) in _index)
        {
            _writer.Write("00dc"u8);
            _writer.Write(0x10); // Each JPEG is a key frame.
            _writer.Write(offset);
            _writer.Write(size);
        }

        EndChunk(index);
        EndChunk(_fileSize);
        WriteAt(_frameCount, FrameCount);
        WriteAt(_streamLength, FrameCount);
        WriteAt(_bufferSize, _largestFrame);
        WriteAt(_streamBufferSize, _largestFrame);
        WriteAt(_bytesPerSecond, (_largestFrame + 9) * _framesPerSecond);
        _writer.Flush();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writer.Dispose();
        }
    }

    private long BeginChunk(ReadOnlySpan<byte> tag, ReadOnlySpan<byte> type = default)
    {
        _writer.Write(tag);
        long size = ReserveInt32();
        _writer.Write(type);
        return size;
    }

    private void EndChunk(long sizePosition)
    {
        int length = checked((int)(_writer.BaseStream.Position - sizePosition - sizeof(int)));
        WriteAt(sizePosition, length);
        if ((length & 1) != 0)
        {
            _writer.Write((byte)0);
        }
    }

    private long ReserveInt32()
    {
        long position = _writer.BaseStream.Position;
        _writer.Write(0);
        return position;
    }

    private void WriteAt(long position, int value)
    {
        long end = _writer.BaseStream.Position;
        _writer.BaseStream.Position = position;
        _writer.Write(value);
        _writer.BaseStream.Position = end;
    }
}
