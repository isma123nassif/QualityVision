using Microsoft.Extensions.Logging.Abstractions;

using OpenCvSharp;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Camera;

namespace QualityVision.Tests.Infrastructure;

public sealed class FrameRecorderTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qv-rec-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Frame MakeFrame(int width, int height, DateTimeOffset at, byte shade)
    {
        byte[] buffer = new byte[width * height * 4];
        Array.Fill(buffer, shade);
        return new Frame(buffer, width, height, 0, at, "test");
    }

    private static int CountFrames(string path, out int width, out int height)
    {
        using var capture = new VideoCapture(path);
        Assert.True(capture.IsOpened());
        width = capture.FrameWidth;
        height = capture.FrameHeight;
        int n = 0;
        using var mat = new Mat();
        while (capture.Read(mat) && !mat.Empty())
        {
            n++;
        }

        return n;
    }

    [Fact]
    public void Recording_WritesReadableFileWithAllFrames()
    {
        string path = Path.Combine(_dir, "sub", "demo.mp4"); // directory is created by the recorder
        using (var recorder = new FrameRecorder(path, 60, NullLogger.Instance))
        {
            for (int i = 0; i < 10; i++)
            {
                using Frame f = MakeFrame(64, 48, T0.AddTicks(i * TimeSpan.TicksPerSecond / 15), (byte)(i * 20));
                recorder.TryWrite(f);
            }

            Assert.Equal(10, recorder.FramesWritten);
        }

        Assert.Equal(10, CountFrames(path, out int w, out int h));
        Assert.Equal(64, w);
        Assert.Equal(48, h);
    }

    [Fact]
    public void Recording_FramesFasterThan15Fps_AreSkipped()
    {
        string path = Path.Combine(_dir, "fast.mp4");
        using var recorder = new FrameRecorder(path, 60, NullLogger.Instance);

        for (int i = 0; i < 30; i++) // 30 fps for one second
        {
            using Frame f = MakeFrame(32, 32, T0.AddTicks(i * TimeSpan.TicksPerSecond / 30), 100);
            recorder.TryWrite(f);
        }

        Assert.InRange(recorder.FramesWritten, 14, 16);
    }

    [Fact]
    public void Recording_20FpsSourceOverTwoSeconds_ReadBackHasAbout30Frames()
    {
        string path = Path.Combine(_dir, "slots.mp4");
        using (var recorder = new FrameRecorder(path, 60, NullLogger.Instance))
        {
            for (int i = 0; i < 40; i++) // 50 ms spacing = 20 fps for 2 s
            {
                using Frame f = MakeFrame(32, 32, T0.AddMilliseconds(i * 50), 100);
                recorder.TryWrite(f);
            }
        }

        Assert.InRange(CountFrames(path, out _, out _), 28, 32);
    }

    [Fact]
    public void Recording_SlowSource_DuplicatesFramesToFillSlots()
    {
        string path = Path.Combine(_dir, "slow.mp4");
        using var recorder = new FrameRecorder(path, 60, NullLogger.Instance);

        for (int i = 0; i < 10; i++) // 5 fps for 2 s: each frame fills 3 slots
        {
            using Frame f = MakeFrame(32, 32, T0.AddMilliseconds(i * 200), 100);
            recorder.TryWrite(f);
        }

        Assert.InRange(recorder.FramesWritten, 27, 30);
    }

    [Fact]
    public void Recording_StopsAfterMaxSeconds()
    {
        string path = Path.Combine(_dir, "max.mp4");
        using var recorder = new FrameRecorder(path, 1, NullLogger.Instance);

        for (int i = 0; i < 30; i++) // 10 fps for three seconds
        {
            using Frame f = MakeFrame(32, 32, T0.AddMilliseconds(i * 100), 100);
            recorder.TryWrite(f);
        }

        Assert.InRange(recorder.FramesWritten, 13, 15); // ~1 s at 15 fps
    }

    [Fact]
    public void Recording_DifferentFrameSize_IsResizedToFirstFrameSize()
    {
        string path = Path.Combine(_dir, "resize.mp4");
        using (var recorder = new FrameRecorder(path, 60, NullLogger.Instance))
        {
            using Frame a = MakeFrame(64, 48, T0, 50);
            using Frame b = MakeFrame(32, 24, T0.AddMilliseconds(70), 150);   // 70 ms = exactly one 15 fps slot, so b is written once
            recorder.TryWrite(a);
            recorder.TryWrite(b);

            Assert.Equal(2, recorder.FramesWritten);
        }

        Assert.Equal(2, CountFrames(path, out int w, out int h));
        Assert.Equal(64, w);
        Assert.Equal(48, h);
    }

    [Fact]
    public void Recording_UnwritablePath_NeverThrowsAndDisables()
    {
        Directory.CreateDirectory(_dir);
        string blocker = Path.Combine(_dir, "file");
        File.WriteAllText(blocker, "x");
        using var recorder = new FrameRecorder(Path.Combine(blocker, "demo.mp4"), 60, NullLogger.Instance);

        using Frame f = MakeFrame(16, 16, T0, 1);
        recorder.TryWrite(f);
        recorder.TryWrite(f);

        Assert.Equal(0, recorder.FramesWritten);
    }
}
