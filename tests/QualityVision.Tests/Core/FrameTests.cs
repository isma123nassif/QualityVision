using QualityVision.Core.Frames;

namespace QualityVision.Tests.Core;

public sealed class FrameTests
{
    private static Frame Create(byte[]? buffer = null, int width = 4, int height = 3, Action<byte[]>? ret = null) =>
        new(buffer ?? new byte[width * 4 * height], width, height, 1, DateTimeOffset.UnixEpoch, "test", ret);

    [Fact]
    public void Ctor_ValidArguments_ExposesProperties()
    {
        using var frame = Create(width: 4, height: 3);

        Assert.Equal(4, frame.Width);
        Assert.Equal(3, frame.Height);
        Assert.Equal(16, frame.Stride);
        Assert.Equal(1, frame.Sequence);
        Assert.Equal("test", frame.SourceName);
        Assert.Equal(48, frame.Pixels.Length);
    }

    [Fact]
    public void Pixels_OversizedBuffer_IsLimitedToStrideTimesHeight()
    {
        using var frame = Create(new byte[1000], 4, 3);

        Assert.Equal(48, frame.Pixels.Length);
    }

    [Fact]
    public void Dispose_CalledTwice_ReturnsBufferOnce()
    {
        var buffer = new byte[48];
        var returned = new List<byte[]>();
        var frame = Create(buffer, ret: returned.Add);

        frame.Dispose();
        frame.Dispose();

        Assert.Single(returned);
        Assert.Same(buffer, returned[0]);
    }

    [Fact]
    public void Dispose_ConcurrentCalls_ReturnsBufferExactlyOnce()
    {
        int count = 0;
        var frame = Create(ret: _ => Interlocked.Increment(ref count));

        Parallel.For(0, 64, _ => frame.Dispose());

        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_WithoutReturnCallback_DoesNotThrow()
    {
        var frame = Create();

        var ex = Record.Exception(frame.Dispose);

        Assert.Null(ex);
    }

    [Fact]
    public void Pixels_AfterDispose_ThrowsObjectDisposedException()
    {
        var frame = Create();
        frame.Dispose();

        Assert.Throws<ObjectDisposedException>(() => frame.Pixels.Length);
    }

    [Fact]
    public void UnsafeBuffer_AfterDispose_ThrowsObjectDisposedException()
    {
        var frame = Create();
        frame.Dispose();

        Assert.Throws<ObjectDisposedException>(() => frame.UnsafeBuffer);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(4, 0)]
    [InlineData(4, -2)]
    public void Ctor_NonPositiveDimensions_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Frame(new byte[100], width, height, 0, DateTimeOffset.UnixEpoch, "x"));
    }

    [Fact]
    public void Ctor_BufferTooSmall_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new Frame(new byte[47], 4, 3, 0, DateTimeOffset.UnixEpoch, "x"));
    }

    [Fact]
    public void Ctor_BufferExactlyRequiredSize_Succeeds()
    {
        using var frame = new Frame(new byte[48], 4, 3, 0, DateTimeOffset.UnixEpoch, "x");

        Assert.Equal(48, frame.Pixels.Length);
    }

    [Fact]
    public void Ctor_NullBuffer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Frame(null!, 4, 3, 0, DateTimeOffset.UnixEpoch, "x"));
    }

    [Fact]
    public void Ctor_NullSourceName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Frame(new byte[48], 4, 3, 0, DateTimeOffset.UnixEpoch, null!));
    }
}
