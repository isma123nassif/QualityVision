using System.Threading.Channels;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Core.Frames;
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

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
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
