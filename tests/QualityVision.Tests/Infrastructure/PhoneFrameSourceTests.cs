using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Camera;

namespace QualityVision.Tests.Infrastructure;

public sealed class PhoneFrameSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class FakeOptionsMonitor(CameraOptions value) : IOptionsMonitor<CameraOptions>
    {
        public CameraOptions CurrentValue => value;
        public CameraOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<CameraOptions, string?> listener) => null;
    }

    private static PhoneFrameSource Create(FakeTimeProvider? time = null, CameraOptions? options = null) =>
        new(new FakeOptionsMonitor(options ?? new CameraOptions()),
            time ?? new FakeTimeProvider(),
            NullLogger<PhoneFrameSource>.Instance);

    private static byte[] Jpeg(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(10, 120, 200));
        Cv2.ImEncode(".jpg", mat, out byte[] bytes);
        return bytes;
    }

    private static async Task<Frame> ReadOneAsync(PhoneFrameSource sut)
    {
        using var cts = new CancellationTokenSource(Timeout);
        await foreach (var frame in sut.ReadFramesAsync(cts.Token))
        {
            return frame;
        }

        throw new InvalidOperationException("No frame");
    }

    [Fact]
    public async Task TryPush_ValidJpeg_YieldsFrameOfExpectedSize()
    {
        await using var sut = Create();

        Assert.True(sut.TryPush(Jpeg(640, 480)));

        using var frame = await ReadOneAsync(sut);
        Assert.Equal(640, frame.Width);
        Assert.Equal(480, frame.Height);
        Assert.Equal(640 * 4, frame.Stride);
        Assert.Equal("Phone", frame.SourceName);
        Assert.Equal(frame.Stride * frame.Height, frame.Pixels.Length);
    }

    [Fact]
    public async Task TryPush_ValidJpeg_SetsCapturedAtFromTimeProvider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        await using var sut = Create(time);

        sut.TryPush(Jpeg(64, 48));

        using var frame = await ReadOneAsync(sut);
        Assert.Equal(time.GetUtcNow(), frame.CapturedAt);
    }

    [Fact]
    public async Task TryPush_FramesPushed_SequenceIncreases()
    {
        await using var sut = Create();
        var jpeg = Jpeg(64, 48);

        sut.TryPush(jpeg);
        using var first = await ReadOneAsync(sut);
        sut.TryPush(jpeg);
        using var second = await ReadOneAsync(sut);

        Assert.True(second.Sequence > first.Sequence);
    }

    [Fact]
    public async Task TryPush_PortraitLargerThanLimit_LongestSideCapped()
    {
        await using var sut = Create(options: new CameraOptions { RequestedWidth = 1280, RequestedHeight = 720 });

        Assert.True(sut.TryPush(Jpeg(1440, 2560)));

        using var frame = await ReadOneAsync(sut);
        Assert.True(Math.Max(frame.Width, frame.Height) <= 1280);
        Assert.True(frame.Height > frame.Width); // stays portrait
        Assert.InRange((double)frame.Width / frame.Height, 1440.0 / 2560 - 0.01, 1440.0 / 2560 + 0.01);
    }

    [Fact]
    public async Task TryPush_Portrait720x1280WithLimit1280_StaysWithinLimit()
    {
        await using var sut = Create(options: new CameraOptions { RequestedWidth = 1280, RequestedHeight = 720 });

        Assert.True(sut.TryPush(Jpeg(720, 1280)));

        using var frame = await ReadOneAsync(sut);
        Assert.True(Math.Max(frame.Width, frame.Height) <= 1280);
        Assert.Equal(720, frame.Width);
        Assert.Equal(1280, frame.Height);
    }

    [Fact]
    public async Task TryPush_ImageSmallerThanLimit_NotUpscaled()
    {
        await using var sut = Create();

        sut.TryPush(Jpeg(320, 240));

        using var frame = await ReadOneAsync(sut);
        Assert.Equal(320, frame.Width);
        Assert.Equal(240, frame.Height);
    }

    [Fact]
    public async Task TryPush_InvalidBytes_ReturnsFalse()
    {
        await using var sut = Create();

        Assert.False(sut.TryPush(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        Assert.Null(sut.LastFrameAt);
    }

    [Fact]
    public async Task TryPush_EmptyInput_ReturnsFalse()
    {
        await using var sut = Create();

        Assert.False(sut.TryPush(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task TryPush_TruncatedJpeg_DoesNotThrow()
    {
        await using var sut = Create();
        var jpeg = Jpeg(640, 480);

        var ex = Record.Exception(() => sut.TryPush(jpeg.AsSpan(0, 20)));

        Assert.Null(ex);
    }

    [Fact]
    public async Task LastFrameAt_NoFrames_IsNull()
    {
        await using var sut = Create();

        Assert.Null(sut.LastFrameAt);
    }

    [Fact]
    public async Task LastFrameAt_AfterPush_UsesTimeProvider()
    {
        var start = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var time = new FakeTimeProvider(start);
        await using var sut = Create(time);

        sut.TryPush(Jpeg(64, 48));
        var first = sut.LastFrameAt;
        time.Advance(TimeSpan.FromSeconds(3));
        sut.TryPush(Jpeg(64, 48));

        Assert.Equal(start, first);
        Assert.Equal(start + TimeSpan.FromSeconds(3), sut.LastFrameAt);
    }

    [Fact]
    public async Task TryPush_TwoFramesBeforeRead_OnlyLatestIsDelivered()
    {
        await using var sut = Create();
        sut.TryPush(Jpeg(64, 48));
        sut.TryPush(Jpeg(128, 96));

        using var frame = await ReadOneAsync(sut);

        Assert.Equal(128, frame.Width);
    }

    [Fact]
    public async Task TryPush_AfterDisposeAsync_ReturnsFalse()
    {
        var sut = Create();
        await sut.DisposeAsync();

        Assert.False(sut.TryPush(Jpeg(64, 48)));
    }

    [Fact]
    public async Task ReadFramesAsync_Cancelled_Throws()
    {
        await using var sut = Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var f in sut.ReadFramesAsync(cts.Token))
            {
                f.Dispose();
            }
        });
    }

    [Fact]
    public void Name_IsPhone()
    {
        Assert.Equal("Phone", Create().Name);
    }
}
