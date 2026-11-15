using QualityVision.Core.Frames;
using QualityVision.Core.Geometry;
using QualityVision.Infrastructure.Imaging;

using SkiaSharp;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Infrastructure;

public sealed class SkiaFrameRendererTests : IDisposable
{
    private readonly SkiaFrameRenderer _renderer = new();

    public void Dispose() => _renderer.Dispose();

    /// <summary>Opaque solid BGRA frame (default black).</summary>
    private static Frame SolidFrame(int width, int height, byte b = 0, byte g = 0, byte r = 0)
    {
        var buffer = new byte[width * height * 4];
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = b;
            buffer[i + 1] = g;
            buffer[i + 2] = r;
            buffer[i + 3] = 255;
        }

        return new Frame(buffer, width, height, 1, DateTimeOffset.UnixEpoch, "test");
    }

    private static SKBitmap Decode(byte[] jpeg)
    {
        SKBitmap? bitmap = SKBitmap.Decode(jpeg);
        Assert.NotNull(bitmap);
        return bitmap;
    }

    private static void AssertJpegHeader(byte[] jpeg)
    {
        Assert.True(jpeg.Length > 2);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
    }

    private static readonly IReadOnlyList<CoreDetection> NoDetections = [];

    private static CoreDetection Box(double x, double y, double w, double h, string cls = "cup") =>
        new(cls, 0.87, new NormalizedRect(x, y, w, h));

    [Fact]
    public void RenderJpeg_NoDetectionsNoZone_ReturnsValidJpegOfFrameSize()
    {
        using Frame frame = SolidFrame(320, 240);

        byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, null, 80, 0);

        AssertJpegHeader(jpeg);
        using SKBitmap bitmap = Decode(jpeg);
        Assert.Equal(320, bitmap.Width);
        Assert.Equal(240, bitmap.Height);
    }

    [Fact]
    public void RenderJpeg_WithDetectionsAndZone_ReturnsValidJpeg()
    {
        using Frame frame = SolidFrame(640, 480);
        var zone = new NormalizedRect(0.1, 0.15, 0.5, 0.55);

        byte[] jpeg = _renderer.RenderJpeg(frame, [Box(0.3, 0.3, 0.2, 0.2), Box(0.6, 0.5, 0.2, 0.3, "bottle")], zone, 80, 0);

        AssertJpegHeader(jpeg);
        using SKBitmap bitmap = Decode(jpeg);
        Assert.Equal(640, bitmap.Width);
        Assert.Equal(480, bitmap.Height);
    }

    [Theory]
    [InlineData(1920, 1080, 1280, 1280, 720)]
    [InlineData(1000, 500, 400, 400, 200)]
    [InlineData(640, 480, 320, 320, 240)]
    public void RenderJpeg_FrameWiderThanMaxWidth_ScalesKeepingAspect(int w, int h, int maxWidth, int expectedW, int expectedH)
    {
        using Frame frame = SolidFrame(w, h);

        byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, null, 80, maxWidth);

        using SKBitmap bitmap = Decode(jpeg);
        Assert.Equal(expectedW, bitmap.Width);
        Assert.Equal(expectedH, bitmap.Height);
    }

    [Theory]
    [InlineData(640)]
    [InlineData(1280)]
    [InlineData(0)]
    [InlineData(-5)]
    public void RenderJpeg_FrameNotWiderThanLimit_IsNotUpscaled(int maxWidth)
    {
        using Frame frame = SolidFrame(640, 360);

        byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, null, 80, maxWidth);

        using SKBitmap bitmap = Decode(jpeg);
        Assert.Equal(640, bitmap.Width);
        Assert.Equal(360, bitmap.Height);
    }

    [Fact]
    public void RenderJpeg_OversizedPooledBuffer_UsesOnlyFrameArea()
    {
        // Pooled buffers can be longer than stride * height.
        var frame = new Frame(new byte[200 * 100 * 4 + 1000], 200, 100, 1, DateTimeOffset.UnixEpoch, "test");
        using (frame)
        {
            byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, null, 80, 0);

            using SKBitmap bitmap = Decode(jpeg);
            Assert.Equal(200, bitmap.Width);
            Assert.Equal(100, bitmap.Height);
        }
    }

    [Fact]
    public void RenderJpeg_DetectionBox_DrawsDefaultCyanEdge()
    {
        using Frame frame = SolidFrame(640, 480);

        byte[] jpeg = _renderer.RenderJpeg(frame, [Box(0.25, 0.4, 0.5, 0.4)], null, 95, 0);

        using SKBitmap bitmap = Decode(jpeg);
        SKColor edge = bitmap.GetPixel((int)(0.25 * 640), (int)(0.6 * 480)); // middle of the left edge
        Assert.True(edge.Blue > 150 && edge.Green > 120 && edge.Red < 100, $"Expected cyan-ish, got {edge}.");
    }

    [Fact]
    public void RenderJpeg_CustomBoxColor_IsUsedInsteadOfDefault()
    {
        using Frame frame = SolidFrame(640, 480);

        byte[] jpeg = _renderer.RenderJpeg(frame, [Box(0.25, 0.4, 0.5, 0.4)], null, 95, 0,
            static _ => SKColors.Red);

        using SKBitmap bitmap = Decode(jpeg);
        SKColor edge = bitmap.GetPixel((int)(0.25 * 640), (int)(0.6 * 480));
        Assert.True(edge.Red > 180 && edge.Green < 100 && edge.Blue < 100, $"Expected red-ish, got {edge}.");
    }

    [Fact]
    public void RenderJpeg_ValidZone_DrawsGreenEdge()
    {
        using Frame frame = SolidFrame(640, 480);
        var zone = new NormalizedRect(0.25, 0.4, 0.5, 0.4);

        byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, zone, 95, 0);

        using SKBitmap bitmap = Decode(jpeg);
        SKColor edge = bitmap.GetPixel((int)(0.25 * 640), (int)(0.6 * 480));
        Assert.True(edge.Green > 150 && edge.Red < 100 && edge.Blue < 140, $"Expected green-ish, got {edge}.");
    }

    [Fact]
    public void RenderJpeg_ZoneAddsPixelsComparedToNoZone()
    {
        using Frame frame = SolidFrame(320, 240);

        byte[] without = _renderer.RenderJpeg(frame, NoDetections, null, 80, 0);
        byte[] with = _renderer.RenderJpeg(frame, NoDetections, new NormalizedRect(0.1, 0.2, 0.5, 0.5), 80, 0);

        Assert.NotEqual(without, with);
    }

    [Fact]
    public void RenderJpeg_InvalidZone_IsIgnored()
    {
        using Frame frame = SolidFrame(320, 240);

        byte[] without = _renderer.RenderJpeg(frame, NoDetections, null, 80, 0);
        byte[] invalid = _renderer.RenderJpeg(frame, NoDetections, new NormalizedRect(0.8, 0.8, 0.5, 0.5), 80, 0);

        Assert.Equal(without, invalid);
    }

    [Fact]
    public void RenderJpeg_BoxAtFrameTopLeft_LabelDoesNotThrow()
    {
        using Frame frame = SolidFrame(320, 240);

        byte[] jpeg = _renderer.RenderJpeg(frame, [Box(0, 0, 0.3, 0.3), Box(0.7, 0.7, 0.3, 0.3)],
            new NormalizedRect(0, 0, 1, 1), 80, 0);

        AssertJpegHeader(jpeg);
    }

    [Fact]
    public void RenderJpeg_TinyOutput_DoesNotThrow()
    {
        using Frame frame = SolidFrame(64, 48);

        byte[] jpeg = _renderer.RenderJpeg(frame, [Box(0.1, 0.1, 0.8, 0.8)], new NormalizedRect(0.05, 0.05, 0.9, 0.9), 80, 32);

        using SKBitmap bitmap = Decode(jpeg);
        Assert.Equal(32, bitmap.Width);
        Assert.Equal(24, bitmap.Height);
    }

    [Fact]
    public void RenderJpeg_DegenerateDetectionBox_IsSkipped()
    {
        using Frame frame = SolidFrame(320, 240);

        byte[] withDegenerate = _renderer.RenderJpeg(frame, [Box(0.5, 0.5, 0, 0)], null, 80, 0);
        byte[] without = _renderer.RenderJpeg(frame, NoDetections, null, 80, 0);

        Assert.Equal(without, withDegenerate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(500)]
    public void RenderJpeg_QualityOutOfRange_IsClamped(int quality)
    {
        using Frame frame = SolidFrame(160, 120);

        byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, null, quality, 0);

        AssertJpegHeader(jpeg);
    }

    [Fact]
    public void RenderJpeg_HigherQuality_ProducesLargerOutput()
    {
        // Noisy content so quality matters.
        var buffer = new byte[320 * 240 * 4];
        new Random(42).NextBytes(buffer);
        using var frame = new Frame(buffer, 320, 240, 1, DateTimeOffset.UnixEpoch, "noise");

        byte[] low = _renderer.RenderJpeg(frame, NoDetections, null, 20, 0);
        byte[] high = _renderer.RenderJpeg(frame, NoDetections, null, 95, 0);

        Assert.True(high.Length > low.Length);
    }

    [Fact]
    public void RenderJpeg_DoesNotModifyFramePixels()
    {
        using Frame frame = SolidFrame(160, 120, b: 10, g: 20, r: 30);
        byte[] before = frame.Pixels.ToArray();

        _ = _renderer.RenderJpeg(frame, [Box(0.1, 0.1, 0.5, 0.5)], new NormalizedRect(0.2, 0.2, 0.5, 0.5), 80, 80);

        Assert.True(before.AsSpan().SequenceEqual(frame.Pixels));
    }

    [Fact]
    public void RenderJpeg_FramePixelColor_SurvivesInOutput()
    {
        // BGRA order check: a pure-red frame must decode as red, not blue.
        using Frame frame = SolidFrame(64, 64, b: 0, g: 0, r: 255);

        byte[] jpeg = _renderer.RenderJpeg(frame, NoDetections, null, 95, 0);

        using SKBitmap bitmap = Decode(jpeg);
        SKColor centre = bitmap.GetPixel(32, 32);
        Assert.True(centre.Red > 200 && centre.Blue < 60, $"Expected red, got {centre}.");
    }

    [Fact]
    public void RenderJpeg_DisposedFrame_Throws()
    {
        Frame frame = SolidFrame(32, 32);
        frame.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _renderer.RenderJpeg(frame, NoDetections, null, 80, 0));
    }

    [Fact]
    public void RenderJpeg_NullArguments_Throw()
    {
        using Frame frame = SolidFrame(32, 32);

        Assert.Throws<ArgumentNullException>(() => _renderer.RenderJpeg(null!, NoDetections, null, 80, 0));
        Assert.Throws<ArgumentNullException>(() => _renderer.RenderJpeg(frame, null!, null, 80, 0));
    }

    [Fact]
    public void Ctor_MissingFont_ThrowsFileNotFound()
    {
        string missing = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "does-not-exist.ttf");

        Assert.Throws<FileNotFoundException>(() => new SkiaFrameRenderer(missing));
    }

    [Fact]
    public void Ctor_BlankFontPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SkiaFrameRenderer(" "));
    }

    [Fact]
    public void Ctor_BundledFont_IsCopiedToOutput()
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DejaVuSans-Bold.ttf")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DejaVu-LICENSE.txt")));
    }
}
