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
using YoloDotNet.ExecutionProvider.Cuda;
using YoloDotNet.Models;

// Alias: inside QualityVision.Infrastructure.*, a bare "Detection" binds to the Infrastructure.Detection namespace.
using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Infrastructure.Detection;

/// <summary>
/// YOLO11 object detector (YoloDotNet, ONNX Runtime). <b>Singleton</b>: the model is loaded once in the
/// constructor and reused for every frame. The execution provider is chosen by <see cref="DetectionOptions.Device"/>
/// (ADR-004): yolo11m on CUDA, or yolo11n on the CPU when forced or as the automatic fallback.
/// Translates YoloDotNet results into Core <see cref="CoreDetection"/> records with normalized boxes, so no
/// YoloDotNet type leaves Infrastructure.
/// </summary>
public sealed class YoloObjectDetector : IObjectDetector, IDisposable
{
    private const int WarmUpSize = 640;
    private const string CpuDeviceName = "CPU";

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

        // Device and model paths are read once: changing them requires a restart (the thresholds are hot-reloadable).
        DetectionOptions current = options.CurrentValue;
        string modelPath;
        long started = Stopwatch.GetTimestamp();
        switch (current.Device)
        {
            case DetectionDevice.Cuda:
                // Forced GPU: a failure (no driver, CUDA 13 or cuDNN 9) propagates and stops startup.
                modelPath = current.ResolveModelPath();
                _yolo = LoadCuda(modelPath, current.GpuId);
                ActiveDevice = CudaDeviceName(current.GpuId);
                break;

            case DetectionDevice.Cpu:
                modelPath = current.ResolveCpuModelPath();
                _yolo = LoadCpu(modelPath);
                ActiveDevice = CpuDeviceName;
                break;

            default:
                // Auto: prefer the GPU with the large model; any failure degrades to the small model on the CPU.
                modelPath = current.ResolveModelPath();
                try
                {
                    _yolo = LoadCuda(modelPath, current.GpuId);
                    ActiveDevice = CudaDeviceName(current.GpuId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "CUDA unavailable ({ExceptionType}: {Reason}); falling back to CPU. Hint: install cuDNN 9 / CUDA 13 drivers",
                        ex.GetType().Name, ex.Message);
                    modelPath = current.ResolveCpuModelPath();
                    started = Stopwatch.GetTimestamp();
                    _yolo = LoadCpu(modelPath);
                    ActiveDevice = CpuDeviceName;
                }

                break;
        }

        ActiveModel = Path.GetFileName(modelPath);
        _logger.LogInformation(
            "YOLO model {ActiveModel} loaded on {ActiveDevice} from {ModelPath} ({SizeMb:F1} MB) in {Ms:F0} ms (requested device: {Device})",
            ActiveModel, ActiveDevice, modelPath, new FileInfo(modelPath).Length / (1024.0 * 1024.0),
            Stopwatch.GetElapsedTime(started).TotalMilliseconds, current.Device);
    }

    /// <summary>Execution device actually in use: <c>"GPU (CUDA {GpuId})"</c> or <c>"CPU"</c>.</summary>
    public string ActiveDevice { get; }

    /// <summary>File name of the loaded model, e.g. <c>yolo11m.onnx</c>.</summary>
    public string ActiveModel { get; }

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

    private static string CudaDeviceName(int gpuId) => $"GPU (CUDA {gpuId})";

    /// <summary>
    /// Loads the model on the CUDA execution provider. The provider constructor throws (OnnxRuntimeException)
    /// when the driver, CUDA 13 or cuDNN 9 is missing. The first CUDA load in a process takes ~5.5 s (context init).
    /// </summary>
    private static Yolo LoadCuda(string modelPath, int gpuId) => new(new YoloOptions
    {
        ExecutionProvider = new CudaExecutionProvider(modelPath, gpuId),
        ImageResize = ImageResize.Proportional,
    });

    private static Yolo LoadCpu(string modelPath) => new(new YoloOptions
    {
        ExecutionProvider = new CpuExecutionProvider(modelPath),
        ImageResize = ImageResize.Proportional,
    });

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
