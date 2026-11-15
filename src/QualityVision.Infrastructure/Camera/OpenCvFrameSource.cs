using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Infrastructure.Camera;

/// <summary>
/// <see cref="IFrameSource"/> over OpenCV <see cref="VideoCapture"/>. One read loop, three modes that differ only
/// in how the capture is opened: <see cref="CameraSourceKind.Url"/> (FFmpeg backend, DroidCam MJPEG / RTSP),
/// <see cref="CameraSourceKind.Webcam"/> (V4L2 index) and <see cref="CameraSourceKind.VideoFile"/> (looped and
/// paced to the file FPS). The capture is opened per enumeration and released when the enumeration ends, so
/// reconnecting = enumerating again (or creating a new instance through <see cref="FrameSourceFactory"/>).
/// </summary>
public sealed class OpenCvFrameSource : IFrameSource
{
    /// <summary>Consecutive empty reads after which a live source is considered lost.</summary>
    public const int MaxConsecutiveEmptyReads = 30;

    /// <summary>Consecutive failed reads (each followed by a rewind) after which a video file is considered broken.</summary>
    private const int MaxConsecutiveFileFailures = 3;

    private const double FallbackFileFps = 25;

    // cv::CAP_PROP_OPEN_TIMEOUT_MSEC / CAP_PROP_READ_TIMEOUT_MSEC (OpenCV >= 4.5.2). OpenCvSharp's
    // VideoCaptureProperties enum has no members for them, so the raw OpenCV ids are used.
    private const int CapPropOpenTimeoutMsec = 53;
    private const int CapPropReadTimeoutMsec = 54;

    /// <summary>FFmpeg open timeout for URL sources (default would be ~30 s against an unreachable host).</summary>
    private const int UrlOpenTimeoutMs = 5000;

    /// <summary>FFmpeg read timeout for URL sources: a stalled stream makes Read return false instead of hanging.</summary>
    private const int UrlReadTimeoutMs = 3000;

    private static readonly TimeSpan EmptyReadDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MaxPlaybackLag = TimeSpan.FromSeconds(1);

    private readonly CameraSourceKind _kind;
    private readonly string _target;
    private readonly int _webcamIndex;
    private readonly int _requestedWidth;
    private readonly int _requestedHeight;
    private readonly int _maxSide;
    private readonly TimeSpan _minEmitInterval;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenCvFrameSource> _logger;

    /// <param name="options">Snapshot of the camera options; values are copied, later changes are ignored.</param>
    /// <param name="contentRootPath">Base directory for a relative <see cref="CameraOptions.VideoFile"/>.</param>
    /// <param name="timeProvider">Clock for pacing and timestamps.</param>
    /// <param name="logger">Logger.</param>
    public OpenCvFrameSource(CameraOptions options, string contentRootPath, TimeProvider timeProvider,
        ILogger<OpenCvFrameSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contentRootPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFps);

        _kind = options.Source;
        _webcamIndex = options.WebcamIndex;
        _requestedWidth = options.RequestedWidth;
        _requestedHeight = options.RequestedHeight;
        _maxSide = options.MaxSide;
        _minEmitInterval = TimeSpan.FromSeconds(1.0 / options.MaxFps);
        _timeProvider = timeProvider;
        _logger = logger;

        switch (_kind)
        {
            case CameraSourceKind.Url:
                _target = options.Url ?? throw new ArgumentException("Camera:Url is required for Source=Url.", nameof(options));
                Name = "Url: " + StripUserInfo(_target);
                break;
            case CameraSourceKind.Webcam:
                _target = string.Empty;
                Name = "Webcam: " + _webcamIndex;
                break;
            case CameraSourceKind.VideoFile:
                string file = options.VideoFile
                    ?? throw new ArgumentException("Camera:VideoFile is required for Source=VideoFile.", nameof(options));
                _target = Path.GetFullPath(file, contentRootPath);
                Name = "VideoFile: " + Path.GetFileName(_target);
                break;
            default:
                throw new ArgumentException($"OpenCvFrameSource does not handle Source={_kind}.", nameof(options));
        }
    }

    public string Name { get; }

    public async IAsyncEnumerable<Frame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Opening a network stream blocks (DNS, TCP, FFmpeg probing), so it runs on the thread pool. Task.Run's token
        // is only checked before the delegate starts, hence WaitAsync: cancellation returns immediately even while
        // the native open is still running (it ends by itself after UrlOpenTimeoutMs at the latest).
        Task<VideoCapture> openTask = Task.Run(Open, CancellationToken.None);
        VideoCapture capture;
        try
        {
            capture = await openTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // We gave up on the open, but it may still succeed later. Ownership of that late capture transfers to
            // this continuation, which disposes it (or observes the open's exception) once the native call returns.
            _ = openTask.ContinueWith(
                static t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        t.Result.Dispose(); // Already completed: does not block.
                    }
                    else
                    {
                        _ = t.Exception; // Observe it so it does not surface as UnobservedTaskException.
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }

        var raw = new Mat();
        Task<bool>? pendingRead = null;
        try
        {
            bool isFile = _kind == CameraSourceKind.VideoFile;
            TimeSpan fileFrameInterval = TimeSpan.FromSeconds(1.0 / GetFileFps(capture));
            long playbackStart = _timeProvider.GetTimestamp();
            long framesPlayed = 0;

            long emitOrigin = _timeProvider.GetTimestamp();
            TimeSpan nextEmit = TimeSpan.Zero;
            TimeSpan emitTolerance = _minEmitInterval / 4;

            int consecutiveFailures = 0;
            long sequence = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // VideoCapture.Read blocks; run it off the async context. WaitAsync keeps cancellation responsive
                // even if a network read hangs (FFmpeg interrupts it after UrlReadTimeoutMs) — see finally.
                pendingRead = Task.Run(() => capture.Read(raw) && !raw.Empty(), CancellationToken.None);
                bool ok = await pendingRead.WaitAsync(cancellationToken).ConfigureAwait(false);
                pendingRead = null;

                if (!ok)
                {
                    consecutiveFailures++;
                    if (isFile)
                    {
                        if (consecutiveFailures >= MaxConsecutiveFileFailures)
                        {
                            throw new FrameSourceException($"{Name}: no frames could be read, even after rewinding.");
                        }

                        // End of file: loop from the start (Plan B video runs forever).
                        capture.Set(VideoCaptureProperties.PosFrames, 0);
                        playbackStart = _timeProvider.GetTimestamp();
                        framesPlayed = 0;
                        continue;
                    }

                    if (consecutiveFailures >= MaxConsecutiveEmptyReads)
                    {
                        throw new FrameSourceException(
                            $"{Name}: {MaxConsecutiveEmptyReads} consecutive empty reads, source lost.");
                    }

                    await Task.Delay(EmptyReadDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                consecutiveFailures = 0;

                if (isFile)
                {
                    // Pace to the file FPS: frame n is due n * interval after playback start.
                    framesPlayed++;
                    TimeSpan due = fileFrameInterval * framesPlayed;
                    TimeSpan elapsed = _timeProvider.GetElapsedTime(playbackStart);
                    if (due > elapsed)
                    {
                        await Task.Delay(due - elapsed, _timeProvider, cancellationToken).ConfigureAwait(false);
                    }
                    else if (elapsed - due > MaxPlaybackLag)
                    {
                        // Fell far behind (e.g. machine stalled): resynchronise instead of fast-forwarding.
                        playbackStart = _timeProvider.GetTimestamp();
                        framesPlayed = 0;
                    }
                }

                // Cap the produced rate at MaxFps. Frames are still read (keeps live latency and file timing
                // correct) but surplus ones are not converted. The tolerance avoids halving the rate when the
                // source runs at exactly MaxFps with jitter.
                TimeSpan now = _timeProvider.GetElapsedTime(emitOrigin);
                if (now + emitTolerance < nextEmit)
                {
                    continue;
                }

                nextEmit += _minEmitInterval;
                if (nextEmit < now)
                {
                    nextEmit = now;
                }

                Frame frame = MatFrameConverter.ToBgraFrame(raw, _maxSide, ++sequence, _timeProvider.GetUtcNow(), Name);

                // Ownership of the frame transfers to the consumer.
                yield return frame;
            }
        }
        finally
        {
            if (pendingRead is { IsCompleted: false } inFlight)
            {
                // A native Read is still writing into raw on a pool thread; disposing now would free memory in use.
                // Ownership of capture and raw transfers to this continuation, which releases them once Read returns.
                _ = inFlight.ContinueWith(
                    t =>
                    {
                        _ = t.Exception; // A faulted read (OpenCVException) must be observed, nobody awaits it.
                        raw.Dispose();
                        capture.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                raw.Dispose();
                capture.Dispose();
            }
        }
    }

    /// <summary>Nothing to release: the capture lives only for the duration of one enumeration.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private VideoCapture Open()
    {
        if (_kind == CameraSourceKind.VideoFile && !File.Exists(_target))
        {
            throw new FrameSourceException($"{Name}: file not found ({_target}).");
        }

        VideoCapture capture = _kind switch
        {
            CameraSourceKind.Webcam => new VideoCapture(_webcamIndex, VideoCaptureAPIs.V4L2),
            // Explicit timeouts must be passed at open time (they configure FFmpeg's interrupt callback).
            CameraSourceKind.Url => new VideoCapture(
                _target,
                VideoCaptureAPIs.FFMPEG,
                [CapPropOpenTimeoutMsec, UrlOpenTimeoutMs, CapPropReadTimeoutMsec, UrlReadTimeoutMs]),
            _ => new VideoCapture(_target, VideoCaptureAPIs.FFMPEG),
        };
        try
        {
            if (!capture.IsOpened())
            {
                throw new FrameSourceException($"{Name}: could not be opened.");
            }

            if (_kind == CameraSourceKind.Webcam)
            {
                // MJPG first: most USB webcams only deliver 720p at a usable FPS in MJPG mode.
                capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
                capture.Set(VideoCaptureProperties.FrameWidth, _requestedWidth);
                capture.Set(VideoCaptureProperties.FrameHeight, _requestedHeight);
                capture.Set(VideoCaptureProperties.Fps, 1.0 / _minEmitInterval.TotalSeconds);
            }

            _logger.LogInformation(
                "Camera opened: {Source} ({Width}x{Height}, reported {Fps:F1} FPS)",
                Name, capture.FrameWidth, capture.FrameHeight, capture.Fps);

            // Ownership of the capture transfers to the caller.
            return capture;
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    private double GetFileFps(VideoCapture capture)
    {
        if (_kind != CameraSourceKind.VideoFile)
        {
            return FallbackFileFps;
        }

        double fps = capture.Fps;
        return double.IsFinite(fps) && fps is > 1 and <= 240 ? fps : FallbackFileFps;
    }

    private static string StripUserInfo(string url)
    {
        // Never put credentials (rtsp://user:pass@host) into names, logs or the dashboard.
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.UserInfo.Length > 0
            ? uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped)
            : url;
    }
}
