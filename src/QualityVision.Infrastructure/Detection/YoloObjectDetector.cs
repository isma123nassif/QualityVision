using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Core.Frames;
using QualityVision.Core.Geometry;
using QualityVision.Infrastructure.Imaging;

using SkiaSharp;

using YoloDotNet;
using YoloDotNet.Enums;
using YoloDotNet.ExecutionProvider.Cpu;
using YoloDotNet.Models;

// Alias: inside QualityVision.Infrastructure.*, a bare "Detection" binds to the Infrastructure.Detection namespace.
using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Infrastructure.Detection;

/// <summary>
/// YOLO11n object detector (YoloDotNet, ONNX Runtime CPU). <b>Singleton</b>: the model is loaded once in the
/// constructor and reused for every frame. Translates YoloDotNet results into Core <see cref="CoreDetection"/> records
/// with normalized boxes, so no YoloDotNet type leaves Infrastructure.
/// </summary>
public sealed class YoloObjectDetector : IObjectDetector, IDisposable
{
    private const int WarmUpSize = 640;

    private readonly Lock _gate = new();
    private readonly IOptionsMonitor<DetectionOptions> _options;
    private readonly ILogger<YoloObjectDetector> _logger;
    private Yolo? _yolo;

    public YoloObjectDetector(IOptionsMonitor<DetectionOptions> options, ILogger<YoloObjectDetector> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;

        // The model path is read once: changing it requires a restart (the thresholds are hot-reloadable).
        string modelPath = options.CurrentValue.ResolveModelPath();
        long started = Stopwatch.GetTimestamp();
        _yolo = new Yolo(new YoloOptions
        {
            ExecutionProvider = new CpuExecutionProvider(modelPath),
            ImageResize = ImageResize.Proportional,
        });

        _logger.LogInformation(
            "YOLO model loaded from {ModelPath} ({SizeMb:F1} MB) in {Ms:F0} ms (CPU execution provider)",
            modelPath, new FileInfo(modelPath).Length / (1024.0 * 1024.0), Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <inheritdoc />
    public IReadOnlyList<CoreDetection> Detect(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // Thresholds are read per frame so a config reload applies immediately.
        DetectionOptions options = _options.CurrentValue;
        int width = frame.Width;
        int height = frame.Height;

        List<ObjectDetection> results;
        lock (_gate)
        {
            Yolo yolo = _yolo ?? throw new ObjectDisposedException(nameof(YoloObjectDetector));

            // Order matters: the bitmap header is disposed before the pin is released (reverse declaration order).
            // Neither outlives this block, so no reference to the frame's pixels survives the call.
            using var pinned = new PinnedFrame(frame);
            using SKBitmap bitmap = pinned.CreateBitmap();
            results = yolo.RunObjectDetection(bitmap, options.Confidence, options.Iou);
        }

        var detections = new List<CoreDetection>(results.Count);
        foreach (ObjectDetection result in results)
        {
            if (ToNormalized(result.BoundingBox, width, height) is { } box)
            {
                detections.Add(new CoreDetection(result.Label.Name, result.Confidence, box));
            }
        }

        return detections;
    }

    /// <summary>Runs one inference on a black 640x640 frame so the first real frame does not pay the JIT/allocation cost.</summary>
    public void WarmUp()
    {
        // Plain (non-pooled) buffer: zeros = black. The frame is owned and disposed here.
        using var frame = new Frame(new byte[WarmUpSize * WarmUpSize * 4], WarmUpSize, WarmUpSize, 0,
            DateTimeOffset.UnixEpoch, "warm-up");
        _ = Detect(frame);
    }

    public void Dispose()
    {
        // Under the lock, so an inference in progress finishes before the native session is released.
        lock (_gate)
        {
            _yolo?.Dispose();
            _yolo = null;
        }
    }

    /// <summary>Pixel box → normalized box clamped to [0,1]; null if nothing of it lies inside the frame.</summary>
    private static NormalizedRect? ToNormalized(SKRectI pixelBox, int width, int height)
    {
        double left = Math.Clamp((double)pixelBox.Left / width, 0, 1);
        double top = Math.Clamp((double)pixelBox.Top / height, 0, 1);
        double right = Math.Clamp((double)pixelBox.Right / width, 0, 1);
        double bottom = Math.Clamp((double)pixelBox.Bottom / height, 0, 1);

        double boxWidth = FitWithinUnit(left, right - left);
        double boxHeight = FitWithinUnit(top, bottom - top);
        return boxWidth > 0 && boxHeight > 0 ? new NormalizedRect(left, top, boxWidth, boxHeight) : null;
    }

    /// <summary>
    /// Guarantees <c>start + size &lt;= 1</c> exactly: <c>a + (b - a)</c> can round one ULP above <c>b</c>,
    /// which would make an edge-touching box fail <see cref="NormalizedRect.IsValid"/>.
    /// </summary>
    private static double FitWithinUnit(double start, double size)
    {
        while (size > 0 && start + size > 1)
        {
            size = Math.BitDecrement(size);
        }

        return size;
    }
}
