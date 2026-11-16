using Microsoft.Extensions.Logging;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Infrastructure.Camera;

/// <summary>
/// Records raw captured frames into an mp4 file (Plan B video for <see cref="CameraSourceKind.VideoFile"/>).
/// Frames are written by time slot at a fixed 15 fps judged by <see cref="Frame.CapturedAt"/>: faster sources are skipped,
/// slower ones get frames duplicated (at most <c>MaxRepeats</c> per frame), so the video plays in real time,
/// the first frame fixes the video size, other sizes are resized. Recording stops after
/// <c>maxSeconds</c> of captured time or on dispose. It never throws: the first error is logged and the recorder disables itself.
/// The frame is only read inside <see cref="TryWrite"/>, so the caller can dispose it afterwards.
/// Not thread-safe: use from the single capture loop.
/// </summary>
public sealed class FrameRecorder : IDisposable
{
    private const double OutputFps = 15;
    private const int MaxRepeats = 5;

    private readonly string _path;
    private readonly TimeSpan _maxDuration;
    private readonly ILogger _logger;
    private VideoWriter? _writer;
    private Size _size;
    private DateTimeOffset _firstAt;
    private long _frames;
    private bool _stopped;

    /// <summary>Creates a recorder that writes to <paramref name="path"/> (overwritten) for at most <paramref name="maxSeconds"/>.</summary>
    public FrameRecorder(string path, int maxSeconds, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);
        _path = path;
        _maxDuration = TimeSpan.FromSeconds(Math.Max(1, maxSeconds));
        _logger = logger;
    }

    /// <summary>Number of frames written so far.</summary>
    public long FramesWritten => _frames;

    /// <summary>Writes <paramref name="frame"/> (BGRA → BGR) if recording is still active. Never throws.</summary>
    public void TryWrite(Frame frame)
    {
        if (_stopped)
        {
            return;
        }

        try
        {
            if (_writer is null)
            {
                Open(frame);
            }
            else if (frame.CapturedAt - _firstAt >= _maxDuration)
            {
                Finish();
                return;
            }

            // 1-based slot this frame belongs to; write it once per missing slot (skip when the slot is already filled).
            long due = (long)Math.Round((frame.CapturedAt - _firstAt).TotalSeconds * OutputFps, MidpointRounding.AwayFromZero) + 1;
            long repeats = Math.Min(due - _frames, MaxRepeats);
            if (repeats > 0)
            {
                Write(frame, (int)repeats);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording to {Path} failed; recording disabled", _path);
            _stopped = true;
            ReleaseWriter();
        }
    }

    /// <summary>Finalizes the file and logs the summary.</summary>
    public void Dispose() => Finish();

    private void Open(Frame frame)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _size = new Size(frame.Width, frame.Height);
        _firstAt = frame.CapturedAt;
        var writer = new VideoWriter();
        if (!writer.Open(_path, FourCC.MP4V, OutputFps, _size) || !writer.IsOpened())
        {
            writer.Dispose();
            throw new InvalidOperationException("OpenCV VideoWriter could not open the output file.");
        }

        _writer = writer;
        _logger.LogInformation("Recording to {Path} ({Width}x{Height}, {Fps} fps)", _path, _size.Width, _size.Height, OutputFps);
    }

    private void Write(Frame frame, int repeats)
    {
        using var bgra = new PinnedMat(frame.UnsafeBuffer, frame.Height, frame.Width, MatType.CV_8UC4, 4);
        using var bgr = new Mat();
        Cv2.CvtColor(bgra.Mat, bgr, ColorConversionCodes.BGRA2BGR);
        if (bgr.Width != _size.Width || bgr.Height != _size.Height)
        {
            using var resized = new Mat();
            Cv2.Resize(bgr, resized, _size);
            WriteRepeated(resized, repeats);
        }
        else
        {
            WriteRepeated(bgr, repeats);
        }
    }

    private void WriteRepeated(Mat image, int repeats)
    {
        for (int i = 0; i < repeats; i++)
        {
            _writer!.Write(image);
            _frames++;
        }
    }

    private void Finish()
    {
        if (_writer is null)
        {
            _stopped = true;
            return;
        }

        ReleaseWriter();
        _stopped = true;
        _logger.LogInformation("Recorded {Frames} frames to {Path}", _frames, _path);
    }

    private void ReleaseWriter()
    {
        try
        {
            _writer?.Release();
            _writer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Closing recording {Path} failed", _path);
        }

        _writer = null;
    }
}
