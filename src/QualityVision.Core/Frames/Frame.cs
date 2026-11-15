namespace QualityVision.Core.Frames;

/// <summary>
/// One captured image in BGRA32 (4 bytes per pixel, stride = Width * 4), backed by a (usually pooled) buffer.
/// The owner MUST dispose it; <see cref="Dispose"/> hands the buffer back through <c>returnBuffer</c>
/// exactly once (idempotent and thread-safe).
/// </summary>
public sealed class Frame : IDisposable
{
    private const int BytesPerPixel = 4;

    private readonly Action<byte[]>? _returnBuffer;
    private readonly int _length;
    private byte[]? _buffer;

    public Frame(byte[] buffer, int width, int height, long sequence, DateTimeOffset capturedAt,
                 string sourceName, Action<byte[]>? returnBuffer = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        long required = (long)width * BytesPerPixel * height;
        if (required > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions are too large.");
        }

        if (buffer.Length < required)
        {
            throw new ArgumentException(
                $"Buffer of {buffer.Length} bytes is too small for a {width}x{height} BGRA frame ({required} bytes).",
                nameof(buffer));
        }

        _buffer = buffer;
        _length = (int)required;
        _returnBuffer = returnBuffer;
        Width = width;
        Height = height;
        Stride = width * BytesPerPixel;
        Sequence = sequence;
        CapturedAt = capturedAt;
        SourceName = sourceName;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row: <c>Width * 4</c>.</summary>
    public int Stride { get; }

    public long Sequence { get; }

    public DateTimeOffset CapturedAt { get; }

    public string SourceName { get; }

    /// <summary>The pixel data (length <c>Stride * Height</c>). Throws <see cref="ObjectDisposedException"/> after dispose.</summary>
    public ReadOnlySpan<byte> Pixels => UnsafeBuffer.AsSpan(0, _length);

    /// <summary>
    /// The raw backing buffer for interop (wrapping in a Mat / SKBitmap). It may be longer than
    /// <c>Stride * Height</c> (pooled arrays) and is valid only until <see cref="Dispose"/>.
    /// </summary>
    public byte[] UnsafeBuffer
    {
        get
        {
            byte[]? buffer = Volatile.Read(ref _buffer);
            ObjectDisposedException.ThrowIf(buffer is null, this);
            return buffer;
        }
    }

    public void Dispose()
    {
        // Interlocked.Exchange guarantees that only one caller ever sees the buffer, so it is returned once.
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            _returnBuffer?.Invoke(buffer);
        }
    }
}
