using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Infrastructure.Camera;

/// <summary>
/// Singleton <see cref="IFrameSource"/> fed by the iPhone capture page: <c>POST /api/camera/frame</c> hands each
/// JPEG to <see cref="TryPush"/>, which decodes it and keeps only the latest frame (capacity 1, DropOldest; the
/// replaced frame is disposed). <see cref="ReadFramesAsync"/> yields those frames to the camera pipeline.
/// </summary>
public sealed class PhoneFrameSource : IFrameSource
{
    /// <summary>Largest width or height (declared in the JPEG header) that <see cref="TryPush"/> will decode.</summary>
    public const int MaxDecodeSide = 4096;

    private readonly Channel<Frame> _latest;
    private readonly IOptionsMonitor<CameraOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PhoneFrameSource> _logger;
    private long _sequence;
    private long _lastFrameUtcTicks;

    public PhoneFrameSource(IOptionsMonitor<CameraOptions> options, TimeProvider timeProvider,
        ILogger<PhoneFrameSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;

        // Concurrent HTTP requests may push at the same time, hence multiple writers.
        _latest = Channel.CreateBounded<Frame>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = false,
            },
            static dropped => dropped.Dispose());
    }

    public string Name => "Phone";

    /// <summary>UTC time of the last successfully decoded frame, or <c>null</c> if none arrived yet.</summary>
    public DateTimeOffset? LastFrameAt
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastFrameUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Decodes a JPEG, caps its longest side at max(RequestedWidth, RequestedHeight) and publishes it as the latest
    /// frame. Returns <c>false</c> if the bytes are not a decodable JPEG, declare a side larger than
    /// <see cref="MaxDecodeSide"/>, or the source has been disposed.
    /// Never blocks on the consumer.
    /// </summary>
    public bool TryPush(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.IsEmpty)
        {
            return false;
        }

        // Decompression-bomb guard: a < 2 MB JPEG can declare up to 2^30 px, and ImDecode would allocate the full
        // bitmap (~3 GB) before any downscale. Check the declared size in the SOFn header first.
        if (!JpegHeader.TryReadSize(jpeg, out int declaredWidth, out int declaredHeight)
            || declaredWidth > MaxDecodeSide || declaredHeight > MaxDecodeSide)
        {
            _logger.LogDebug(
                "Rejected phone frame: no valid JPEG frame header or declared size {Width}x{Height} exceeds {MaxSide}",
                declaredWidth, declaredHeight, MaxDecodeSide);
            return false;
        }

        Frame frame;
        try
        {
            using Mat decoded = Cv2.ImDecode(jpeg, ImreadModes.Color);
            if (decoded.Empty())
            {
                _logger.LogDebug("Rejected phone frame: {Length} bytes could not be decoded", jpeg.Length);
                return false;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            frame = MatFrameConverter.ToBgraFrame(
                decoded, _options.CurrentValue.MaxSide, Interlocked.Increment(ref _sequence), now, Name);
        }
        catch (OpenCVException ex)
        {
            _logger.LogDebug(ex, "Rejected phone frame: {Length} bytes could not be decoded", jpeg.Length);
            return false;
        }

        // Ownership of the frame transfers to the channel (and then to the reader). With DropOldest TryWrite only
        // fails once the channel is completed; in that case nobody will read the frame, so release it here.
        if (!_latest.Writer.TryWrite(frame))
        {
            frame.Dispose();
            return false;
        }

        Interlocked.Exchange(ref _lastFrameUtcTicks, frame.CapturedAt.UtcTicks);
        return true;
    }

    /// <summary>Yields pushed frames; the consumer owns and disposes each one.</summary>
    public async IAsyncEnumerable<Frame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (Frame frame in _latest.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Ownership of the frame transfers to the consumer.
            yield return frame;
        }
    }

    /// <summary>Completes the channel and disposes a frame that was never read. Called by the DI container.</summary>
    public ValueTask DisposeAsync()
    {
        _latest.Writer.TryComplete();
        while (_latest.Reader.TryRead(out Frame? pending))
        {
            pending.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
