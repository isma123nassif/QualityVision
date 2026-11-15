using System.Threading.Channels;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Core.Frames;
using QualityVision.Core.Geometry;
using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Detection;
using QualityVision.Infrastructure.Imaging;
using QualityVision.Web.Pipeline;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Web;

/// <summary>
/// Needs <c>models/yolo11n.onnx</c> in the test output because <see cref="DetectionService"/> takes the concrete
/// <see cref="YoloObjectDetector"/> for warm-up. Filter with <c>--filter "Category!=Integration"</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DetectionServiceTests
{
    private sealed class ThrowingDetector : IObjectDetector
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<CoreDetection> Detect(Frame frame)
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("boom");
        }
    }

    /// <summary>Runs one scripted step per Detect call; once the script is exhausted it yields no detections.</summary>
    private sealed class ScriptedDetector(params Func<IReadOnlyList<CoreDetection>>[] steps) : IObjectDetector
    {
        private int _index;

        public IReadOnlyList<CoreDetection> Detect(Frame frame)
        {
            int i = Interlocked.Increment(ref _index) - 1;
            return i < steps.Length ? steps[i]() : [];
        }
    }

    private sealed class FixedRuleSetProvider : IRuleSetProvider
    {
        public RuleSet Current { get; } = new(
        [
            new KeyValuePair<string, Verdict>("bottle", Verdict.Ok),
            new KeyValuePair<string, Verdict>("cup", Verdict.Nok),
        ]);
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    // Default zone is x 0.3–0.7, y 0.2–0.8.
    private static CoreDetection InZone(string cls) => new(cls, 0.9, new NormalizedRect(0.45, 0.45, 0.1, 0.1));

    private static CoreDetection OutsideZone(string cls) => new(cls, 0.9, new NormalizedRect(0.0, 0.0, 0.1, 0.1));

    /// <summary>Feeds one frame per script step and waits until each one has been published.</summary>
    private static async Task<InspectionLog> RunAsync(params Func<IReadOnlyList<CoreDetection>>[] steps)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var channel = Channel.CreateUnbounded<Frame>();
        using var yolo = new YoloObjectDetector(
            new FixedOptionsMonitor<DetectionOptions>(new DetectionOptions()),
            NullLogger<YoloObjectDetector>.Instance);
        using var renderer = new SkiaFrameRenderer();
        var broadcaster = new FrameBroadcaster();
        var log = new InspectionLog();
        using var service = new DetectionService(
            channel,
            new ScriptedDetector(steps),
            yolo,
            renderer,
            new PipelineMetrics(TimeProvider.System),
            broadcaster,
            new FixedOptionsMonitor<StreamOptions>(new StreamOptions { MaxFps = 60 }),
            new FixedOptionsMonitor<InspectionOptions>(new InspectionOptions()),
            new FixedRuleSetProvider(),
            log,
            TimeProvider.System,
            NullLogger<DetectionService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            long seen = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                channel.Writer.TryWrite(new Frame(new byte[64 * 48 * 4], 64, 48, i + 1, DateTimeOffset.UnixEpoch, "test"));
                (_, seen) = await broadcaster.WaitForNextAsync(seen, cts.Token);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        return log;
    }

    [Fact]
    public async Task ExecuteAsync_PresentThenDetectorThrowsThenPresent_CountsOnce()
    {
        Func<IReadOnlyList<CoreDetection>> present = () => [InZone("cup")];
        Func<IReadOnlyList<CoreDetection>> fail = () => throw new InvalidOperationException("boom");

        InspectionLog log = await RunAsync(present, fail, present);

        Assert.Equal(1, log.Counts.Total);
        Assert.Equal(1, log.Counts.Nok);
    }

    [Fact]
    public async Task ExecuteAsync_ObjectOutsideZone_IsNotLogged()
    {
        InspectionLog log = await RunAsync(() => [OutsideZone("cup")], () => [OutsideZone("cup")]);

        Assert.Equal(0, log.Counts.Total);
        Assert.Empty(log.Recent());
    }

    [Fact]
    public async Task ExecuteAsync_TwoDistinctClassesInZone_GetSequences1And2()
    {
        InspectionLog log = await RunAsync(() => [InZone("cup"), InZone("bottle")]);

        var events = log.Recent().OrderBy(e => e.Sequence).ToArray();
        Assert.Equal([1L, 2L], events.Select(e => e.Sequence).ToArray());
        Assert.Equal(["bottle", "cup"], events.Select(e => e.ClassName).Order().ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_DetectorThrows_StillPublishesFrameToBroadcaster()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var channel = Channel.CreateUnbounded<Frame>();
        var detector = new ThrowingDetector();
        using var yolo = new YoloObjectDetector(
            new FixedOptionsMonitor<DetectionOptions>(new DetectionOptions()),
            NullLogger<YoloObjectDetector>.Instance);
        using var renderer = new SkiaFrameRenderer();
        var metrics = new PipelineMetrics(TimeProvider.System);
        var broadcaster = new FrameBroadcaster();
        using var service = new DetectionService(
            channel,
            detector,
            yolo,
            renderer,
            metrics,
            broadcaster,
            new FixedOptionsMonitor<StreamOptions>(new StreamOptions { MaxFps = 60 }),
            new FixedOptionsMonitor<InspectionOptions>(new InspectionOptions()),
            new FixedRuleSetProvider(),
            new InspectionLog(),
            TimeProvider.System,
            NullLogger<DetectionService>.Instance);

        await service.StartAsync(cts.Token);
        try
        {
            channel.Writer.TryWrite(new Frame(new byte[64 * 48 * 4], 64, 48, 1, DateTimeOffset.UnixEpoch, "test"));

            (byte[] jpeg, long sequence) = await broadcaster.WaitForNextAsync(0, cts.Token);

            Assert.True(detector.Calls >= 1);
            Assert.Equal(1, sequence);
            Assert.True(jpeg.Length > 2);
            Assert.Equal(0xFF, jpeg[0]); // JPEG SOI marker
            Assert.Equal(0xD8, jpeg[1]);
            Assert.Equal(0, metrics.FramesProcessed); // a failed inference is not a processed frame
            Assert.NotNull(metrics.SinceLastFrame);
            Assert.Null(metrics.SinceLastSuccess);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }
}
