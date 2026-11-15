using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using OpenCvSharp;

using QualityVision.Infrastructure.Camera;

namespace QualityVision.Tests.Infrastructure;

public sealed class JpegHeaderTests
{
    private sealed class FakeOptionsMonitor(CameraOptions value) : IOptionsMonitor<CameraOptions>
    {
        public CameraOptions CurrentValue => value;
        public CameraOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<CameraOptions, string?> listener) => null;
    }

    private static PhoneFrameSource CreateSource() =>
        new(new FakeOptionsMonitor(new CameraOptions()), new FakeTimeProvider(), NullLogger<PhoneFrameSource>.Instance);

    private static byte[] Jpeg(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(10, 120, 200));
        Cv2.ImEncode(".jpg", mat, out byte[] bytes);
        return bytes;
    }

    private static int FindSof(byte[] jpeg)
    {
        for (int i = 2; i < jpeg.Length - 1; i++)
        {
            if (jpeg[i] == 0xFF && (jpeg[i + 1] == 0xC0 || jpeg[i + 1] == 0xC1 || jpeg[i + 1] == 0xC2))
            {
                return i;
            }
        }

        throw new InvalidOperationException("No SOF marker found");
    }

    private static byte[] WithDeclaredSize(byte[] jpeg, int width, int height)
    {
        byte[] copy = (byte[])jpeg.Clone();
        int sof = FindSof(copy);
        copy[sof + 5] = (byte)(height >> 8);
        copy[sof + 6] = (byte)height;
        copy[sof + 7] = (byte)(width >> 8);
        copy[sof + 8] = (byte)width;
        return copy;
    }

    [Theory]
    [InlineData(64, 48)]
    [InlineData(640, 480)]
    [InlineData(480, 640)]
    [InlineData(1, 1)]
    public void TryReadSize_RealJpeg_ReturnsCorrectSize(int w, int h)
    {
        bool ok = JpegHeader.TryReadSize(Jpeg(w, h), out int width, out int height);

        Assert.True(ok);
        Assert.Equal(w, width);
        Assert.Equal(h, height);
    }

    [Fact]
    public void TryReadSize_Empty_ReturnsFalse()
    {
        Assert.False(JpegHeader.TryReadSize(ReadOnlySpan<byte>.Empty, out int w, out int h));
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }

    [Fact]
    public void TryReadSize_Garbage_ReturnsFalse()
    {
        Assert.False(JpegHeader.TryReadSize(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, out _, out _));
    }

    [Fact]
    public void TryReadSize_OnlySoi_ReturnsFalse()
    {
        Assert.False(JpegHeader.TryReadSize(new byte[] { 0xFF, 0xD8 }, out _, out _));
    }

    [Fact]
    public void TryReadSize_TruncatedBeforeFrameHeader_ReturnsFalse()
    {
        byte[] jpeg = Jpeg(640, 480);
        int sof = FindSof(jpeg);

        Assert.False(JpegHeader.TryReadSize(jpeg.AsSpan(0, sof + 3), out _, out _));
    }

    [Fact]
    public void TryReadSize_TruncatedInsideDimensions_ReturnsFalse()
    {
        byte[] jpeg = Jpeg(640, 480);
        int sof = FindSof(jpeg);

        Assert.False(JpegHeader.TryReadSize(jpeg.AsSpan(0, sof + 7), out _, out _));
    }

    [Fact]
    public void TryReadSize_ZeroDimension_ReturnsFalse()
    {
        byte[] jpeg = WithDeclaredSize(Jpeg(64, 48), 0, 48);

        Assert.False(JpegHeader.TryReadSize(jpeg, out _, out _));
    }

    [Fact]
    public void TryReadSize_PatchedHeader_ReturnsDeclaredSize()
    {
        byte[] jpeg = WithDeclaredSize(Jpeg(64, 48), 10000, 9000);

        Assert.True(JpegHeader.TryReadSize(jpeg, out int w, out int h));
        Assert.Equal(10000, w);
        Assert.Equal(9000, h);
    }

    [Fact]
    public async Task TryPush_HeaderDeclares10000x10000_ReturnsFalse()
    {
        await using var sut = CreateSource();
        byte[] bomb = WithDeclaredSize(Jpeg(64, 48), 10000, 10000);

        Assert.False(sut.TryPush(bomb));
        Assert.Null(sut.LastFrameAt);
    }

    [Fact]
    public async Task TryPush_OneSideAboveMax_ReturnsFalse()
    {
        await using var sut = CreateSource();
        byte[] jpeg = WithDeclaredSize(Jpeg(64, 48), 64, PhoneFrameSource.MaxDecodeSide + 1);

        Assert.False(sut.TryPush(jpeg));
    }
}
