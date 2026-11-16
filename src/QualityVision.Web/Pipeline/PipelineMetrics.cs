using QualityVision.Core.Rules;

namespace QualityVision.Web.Pipeline;

/// <summary>Read-only view of the detection pipeline's live numbers (for the dashboard and the metrics endpoint).</summary>
public interface IPipelineMetrics
{
    /// <summary>Frames through detection per second (1 s window).</summary>
    double ProcessedFps { get; }

    /// <summary>Exponential moving average of the inference time in ms (alpha 0.2).</summary>
    double InferenceMs { get; }

    double LastInferenceMs { get; }

    /// <summary>Immutable snapshot of the classified detections of the latest processed frame.</summary>
    IReadOnlyList<ClassifiedDetection> LastDetections { get; }

    long FramesProcessed { get; }

    /// <summary>True once at least one inference (warm-up counts) has succeeded.</summary>
    bool ModelReady { get; }

    /// <summary>Time since the last successful inference, or null if none has happened yet.</summary>
    TimeSpan? SinceLastSuccess { get; }

    /// <summary>Time since the last frame reached detection (success or not), or null if none has.</summary>
    TimeSpan? SinceLastFrame { get; }

    /// <summary>Active inference device, e.g. "GPU (CUDA 0)" or "CPU"; empty until the detector reported it.</summary>
    string Device { get; }

    /// <summary>File name of the active model, e.g. "yolo11m.onnx"; empty until the detector reported it.</summary>
    string Model { get; }
}

/// <summary>Thread-safe singleton: written by <see cref="DetectionService"/>, read by UI/endpoints.</summary>
public sealed class PipelineMetrics(TimeProvider timeProvider) : IPipelineMetrics
{
    private const double EmaAlpha = 0.2;
    private static readonly TimeSpan FpsWindow = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private IReadOnlyList<ClassifiedDetection> _lastDetections = [];
    private double _fps;
    private double _ema;
    private double _last;
    private long _framesProcessed;
    private long _windowStart;
    private int _windowCount;
    private bool _windowStarted;
    private bool _hasSample;
    private volatile bool _modelReady;
    private long _lastSuccessTimestamp;
    private long _lastFrameTimestamp;
    private volatile bool _hasSuccess;
    private volatile bool _hasFrame;
    private string _device = string.Empty;
    private string _model = string.Empty;

    public string Device => Volatile.Read(ref _device);

    public string Model => Volatile.Read(ref _model);

    /// <summary>Records the device and model the detector selected (set once at startup).</summary>
    public void SetModelInfo(string device, string model)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(model);
        Volatile.Write(ref _device, device);
        Volatile.Write(ref _model, model);
    }

    public double ProcessedFps
    {
        get
        {
            lock (_gate)
            {
                // No frames for two windows: the rate is zero, not the stale last value.
                if (_windowStarted && timeProvider.GetElapsedTime(_windowStart) >= FpsWindow * 2)
                {
                    return 0;
                }

                return _fps;
            }
        }
    }

    public double InferenceMs
    {
        get
        {
            lock (_gate)
            {
                return _ema;
            }
        }
    }

    public double LastInferenceMs
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    public IReadOnlyList<ClassifiedDetection> LastDetections => Volatile.Read(ref _lastDetections);

    public long FramesProcessed => Interlocked.Read(ref _framesProcessed);

    public bool ModelReady => _modelReady;

    public TimeSpan? SinceLastSuccess =>
        _hasSuccess ? timeProvider.GetElapsedTime(Interlocked.Read(ref _lastSuccessTimestamp)) : null;

    public TimeSpan? SinceLastFrame =>
        _hasFrame ? timeProvider.GetElapsedTime(Interlocked.Read(ref _lastFrameTimestamp)) : null;

    /// <summary>Notes that a frame reached detection (call for every frame, also failing ones).</summary>
    public void MarkFrameSeen()
    {
        Interlocked.Exchange(ref _lastFrameTimestamp, timeProvider.GetTimestamp());
        _hasFrame = true;
    }

    /// <summary>Marks the model usable (warm-up inference succeeded).</summary>
    public void MarkModelReady() => _modelReady = true;

    /// <summary>Records one processed frame.</summary>
    public void Record(IReadOnlyList<ClassifiedDetection> detections, double inferenceMs)
    {
        ArgumentNullException.ThrowIfNull(detections);

        // Copy so later mutation by the caller cannot leak into the published snapshot.
        Volatile.Write(ref _lastDetections, detections.ToArray());
        Interlocked.Increment(ref _framesProcessed);
        _modelReady = true;

        long now = timeProvider.GetTimestamp();
        Interlocked.Exchange(ref _lastSuccessTimestamp, now);
        _hasSuccess = true;
        lock (_gate)
        {
            _last = inferenceMs;
            _ema = _hasSample ? (EmaAlpha * inferenceMs) + ((1 - EmaAlpha) * _ema) : inferenceMs;
            _hasSample = true;

            if (!_windowStarted)
            {
                _windowStarted = true;
                _windowStart = now;
                _windowCount = 0;
            }

            _windowCount++;
            TimeSpan elapsed = timeProvider.GetElapsedTime(_windowStart, now);
            if (elapsed >= FpsWindow)
            {
                _fps = _windowCount / elapsed.TotalSeconds;
                _windowStart = now;
                _windowCount = 0;
            }
        }
    }
}
