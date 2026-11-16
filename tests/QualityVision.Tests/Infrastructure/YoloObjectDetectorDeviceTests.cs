using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Detection;

using CoreDetection = QualityVision.Core.Detection.Detection;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace QualityVision.Tests.Infrastructure;

/// <summary>
/// Execution-provider selection (ADR-004, M4b). Needs <c>models/yolo11n.onnx</c>, <c>models/yolo11m.onnx</c> and
/// <c>Assets/bus.jpg</c> in the test output. Only the forced-CUDA test touches the GPU, and it is a no-op
/// without one; exclude it with <c>--filter "Category!=Gpu"</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class YoloObjectDetectorDeviceTests
{
    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CapturingLogger : ILogger<YoloObjectDetector>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static string ModelFile(string name) => Path.Combine(AppContext.BaseDirectory, "models", name);

    private static DetectionOptions Options(DetectionDevice device) => new()
    {
        Device = device,
        ModelPath = ModelFile("yolo11m.onnx"),
        CpuModelPath = ModelFile("yolo11n.onnx"),
    };

    private static YoloObjectDetector Create(DetectionOptions options, ILogger<YoloObjectDetector>? logger = null) =>
        new(new FixedOptionsMonitor<DetectionOptions>(options), logger ?? NullLogger<YoloObjectDetector>.Instance);

    private static Frame LoadBus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "bus.jpg");
        using Mat bgr = Cv2.ImRead(path, ImreadModes.Color);
        Assert.False(bgr.Empty(), "bus.jpg could not be decoded.");
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);

        var buffer = new byte[bgra.Width * bgra.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, buffer, 0, buffer.Length);
        return new Frame(buffer, bgra.Width, bgra.Height, 1, DateTimeOffset.UnixEpoch, "bus.jpg");
    }

    private static void AssertFindsBusAndPerson(YoloObjectDetector detector)
    {
        using Frame frame = LoadBus();

        IReadOnlyList<CoreDetection> detections = detector.Detect(frame);

        Assert.Contains(detections, d => d.ClassName == "bus" && d.Confidence >= 0.5);
        Assert.Contains(detections, d => d.ClassName == "person" && d.Confidence >= 0.5);
    }

    [Fact]
    public void OnnxRuntimeNativeLibrary_InOutput_IsGpuBuild()
    {
        // Hardware independent: the GPU build lists CUDAExecutionProvider even without a GPU. The CPU-only build
        // does not, so this fails if a project's output got the CPU libonnxruntime.so (ADR-004, ExcludeAssets).
        Assert.Contains("CUDAExecutionProvider", OrtEnv.Instance().GetAvailableProviders());
    }

    [Fact]
    public void Ctor_ForcedCpu_ReportsCpuAndSmallModelAndDetectsBus()
    {
        using YoloObjectDetector detector = Create(Options(DetectionDevice.Cpu));

        Assert.Equal("CPU", detector.ActiveDevice);
        Assert.Equal("yolo11n.onnx", detector.ActiveModel);
        AssertFindsBusAndPerson(detector);
    }

    [Fact]
    public void Ctor_Auto_ReportsConsistentDeviceAndModelAndDetectsBus()
    {
        using YoloObjectDetector detector = Create(Options(DetectionDevice.Auto));

        Assert.False(string.IsNullOrWhiteSpace(detector.ActiveDevice));
        Assert.False(string.IsNullOrWhiteSpace(detector.ActiveModel));

        // Either outcome is valid, but device and model must agree (GPU ⇒ m model, CPU ⇒ n model).
        if (detector.ActiveDevice == "CPU")
        {
            Assert.Equal("yolo11n.onnx", detector.ActiveModel);
        }
        else
        {
            Assert.Equal("GPU (CUDA 0)", detector.ActiveDevice);
            Assert.Equal("yolo11m.onnx", detector.ActiveModel);
        }

        AssertFindsBusAndPerson(detector);
    }

    [Fact]
    public void Ctor_AutoWithUnusableGpuModel_FallsBackToCpuAndLogsWarning()
    {
        // A corrupt GPU model makes the CUDA load fail on any machine, with or without a GPU.
        string corrupt = Path.Combine(Path.GetTempPath(), $"qv-corrupt-{Guid.NewGuid():N}.onnx");
        File.WriteAllText(corrupt, "not an onnx model");
        try
        {
            var logger = new CapturingLogger();
            DetectionOptions options = Options(DetectionDevice.Auto);
            options.ModelPath = corrupt;

            using YoloObjectDetector detector = Create(options, logger);

            Assert.Equal("CPU", detector.ActiveDevice);
            Assert.Equal("yolo11n.onnx", detector.ActiveModel);
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("cuDNN", StringComparison.Ordinal));
            AssertFindsBusAndPerson(detector);
        }
        finally
        {
            File.Delete(corrupt);
        }
    }

    [Fact]
    public void Ctor_ForcedCudaWithUnusableModel_Throws()
    {
        // Forced CUDA never falls back: a failed load is fatal (here guaranteed by a missing model file).
        DetectionOptions options = Options(DetectionDevice.Cuda);
        options.ModelPath = Path.Combine(Path.GetTempPath(), $"qv-missing-{Guid.NewGuid():N}.onnx");

        Assert.ThrowsAny<Exception>(() => Create(options));
    }

    [Fact]
    [Trait("Category", "Gpu")]
    public void Ctor_ForcedCuda_ReportsGpuAndMediumModelAndDetectsBus_WhenCudaAvailable()
    {
        YoloObjectDetector detector;
        try
        {
            detector = Create(Options(DetectionDevice.Cuda));
        }
        catch (Exception ex)
        {
            // xUnit v2 cannot skip at runtime: with no usable CUDA/cuDNN this test is a deliberate no-op.
            Console.WriteLine($"CUDA unavailable, forced-Cuda test not run: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        using (detector)
        {
            Assert.Equal("GPU (CUDA 0)", detector.ActiveDevice);
            Assert.Equal("yolo11m.onnx", detector.ActiveModel);
            AssertFindsBusAndPerson(detector);
        }
    }
}
