using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Detection;
using QualityVision.Infrastructure.Imaging;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Infrastructure;

/// <summary>
/// Real-model tests (ONNX Runtime on CPU, ~50 ms per inference). They need <c>models/yolo11n.onnx</c> and
/// <c>Assets/bus.jpg</c> in the test output; filter them out with <c>--filter "Category!=Integration"</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class YoloObjectDetectorIntegrationTests : IDisposable
{
    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; set; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private readonly YoloObjectDetector _detector;
    private readonly FixedOptionsMonitor<DetectionOptions> _options;

    public YoloObjectDetectorIntegrationTests()
    {
        _options = new FixedOptionsMonitor<DetectionOptions>(new DetectionOptions
        {
            ModelPath = Path.Combine(AppContext.BaseDirectory, "models", "yolo11n.onnx"),
            Confidence = 0.45,
            Iou = 0.7,
        });
        _detector = new YoloObjectDetector(_options, NullLogger<YoloObjectDetector>.Instance);
    }

    public void Dispose() => _detector.Dispose();

    private static Frame LoadBus(double scale = 1.0)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "bus.jpg");
        using Mat bgr = Cv2.ImRead(path, ImreadModes.Color);
        Assert.False(bgr.Empty(), "bus.jpg could not be decoded.");
        using var scaled = new Mat();
        if (scale == 1.0)
        {
            bgr.CopyTo(scaled);
        }
        else
        {
            Cv2.Resize(bgr, scaled, new OpenCvSharp.Size((int)(bgr.Width * scale), (int)(bgr.Height * scale)));
        }

        using var bgra = new Mat();
        Cv2.CvtColor(scaled, bgra, ColorConversionCodes.BGR2BGRA);

        var buffer = new byte[bgra.Width * bgra.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, buffer, 0, buffer.Length);
        return new Frame(buffer, bgra.Width, bgra.Height, 1, DateTimeOffset.UnixEpoch, "bus.jpg");
    }

    [Fact]
    public void Detect_BusImage_FindsPersonAndBusWithValidBoxes()
    {
        using Frame frame = LoadBus();

        IReadOnlyList<CoreDetection> detections = _detector.Detect(frame);

        Assert.Contains(detections, d => d.ClassName == "person" && d.Confidence >= 0.5);
        Assert.Contains(detections, d => d.ClassName == "bus" && d.Confidence >= 0.5);
        Assert.All(detections, d => Assert.True(d.Box.IsValid, $"{d.ClassName} box {d.Box} is not valid."));
        Assert.All(detections, d => Assert.InRange(d.Confidence, 0.45, 1.0));
    }

    [Fact]
    public void Detect_BusImage_BusBoxCoversMostOfTheFrame()
    {
        using Frame frame = LoadBus();

        CoreDetection bus = _detector.Detect(frame).First(d => d.ClassName == "bus");

        Assert.True(bus.Box.Width > 0.5, $"Bus width {bus.Box.Width}");
        Assert.True(bus.Box.Height > 0.4, $"Bus height {bus.Box.Height}");
    }

    [Fact]
    public void Detect_SameFrameTwice_IsDeterministic()
    {
        using Frame frame = LoadBus();

        IReadOnlyList<CoreDetection> first = _detector.Detect(frame);
        IReadOnlyList<CoreDetection> second = _detector.Detect(frame);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].ClassName, second[i].ClassName);
            Assert.Equal(first[i].Confidence, second[i].Confidence, 4);
        }
    }

    [Fact]
    public void Detect_DoesNotModifyFramePixels()
    {
        using Frame frame = LoadBus();
        byte[] before = frame.Pixels.ToArray();

        _ = _detector.Detect(frame);

        Assert.True(before.AsSpan().SequenceEqual(frame.Pixels));
    }

    [Fact]
    public void Detect_FrameStillUsableAfterReturn_NoReferenceKept()
    {
        Frame frame = LoadBus();

        _ = _detector.Detect(frame);
        frame.Dispose();

        // A second, different frame works after the first was disposed.
        using Frame other = LoadBus();
        Assert.NotEmpty(_detector.Detect(other));
    }

    [Fact]
    public void Detect_ScaledDownImage_BoxesStayNormalizedAndValid()
    {
        using Frame half = LoadBus(0.5);

        IReadOnlyList<CoreDetection> detections = _detector.Detect(half);

        Assert.Contains(detections, d => d.ClassName == "bus");
        Assert.All(detections, d => Assert.True(d.Box.IsValid));
    }

    [Fact]
    public void Detect_BlackFrame_ReturnsNoDetections()
    {
        using var frame = new Frame(new byte[640 * 480 * 4], 640, 480, 1, DateTimeOffset.UnixEpoch, "black");

        Assert.Empty(_detector.Detect(frame));
    }

    [Fact]
    public void Detect_HigherConfidenceThreshold_ReturnsSubsetAtRuntime()
    {
        using Frame frame = LoadBus();
        int atDefault = _detector.Detect(frame).Count;

        _options.CurrentValue = new DetectionOptions
        {
            ModelPath = _options.CurrentValue.ModelPath,
            Confidence = 0.88,
            Iou = 0.7,
        };
        IReadOnlyList<CoreDetection> strict = _detector.Detect(frame);

        Assert.True(strict.Count < atDefault, $"{strict.Count} should be fewer than {atDefault}.");
        Assert.All(strict, d => Assert.True(d.Confidence >= 0.88));
    }

    [Fact]
    public void WarmUp_Succeeds()
    {
        _detector.WarmUp();
    }

    [Fact]
    public void Detect_AfterDispose_ThrowsObjectDisposed()
    {
        using Frame frame = LoadBus();
        _detector.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _detector.Detect(frame));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _detector.Dispose();
        _detector.Dispose();
    }

    [Fact]
    public void Detect_NullFrame_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _detector.Detect(null!));
    }

    [Fact]
    public void Ctor_MissingModel_Throws()
    {
        var options = new FixedOptionsMonitor<DetectionOptions>(new DetectionOptions { ModelPath = "models/missing.onnx" });

        Assert.ThrowsAny<Exception>(() => new YoloObjectDetector(options, NullLogger<YoloObjectDetector>.Instance));
    }

    [Fact]
    public void RenderedBusImage_WithRealDetections_IsValidJpeg()
    {
        using Frame frame = LoadBus();
        IReadOnlyList<CoreDetection> detections = _detector.Detect(frame);
        using var renderer = new SkiaFrameRenderer();

        byte[] jpeg = renderer.RenderJpeg(frame, detections, new QualityVision.Core.Geometry.NormalizedRect(0.1, 0.1, 0.5, 0.5), 80, 1280);

        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
    }
}
