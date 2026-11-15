using Microsoft.Extensions.Time.Testing;

using QualityVision.Core.Geometry;
using QualityVision.Web.Pipeline;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Web;

public sealed class PipelineMetricsTests
{
    private static readonly IReadOnlyList<CoreDetection> None = [];

    private static CoreDetection Cup() => new("cup", 0.8, new NormalizedRect(0.1, 0.1, 0.2, 0.2));

    [Fact]
    public void NewInstance_AllValuesAreZeroOrEmpty()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        Assert.Equal(0, metrics.ProcessedFps);
        Assert.Equal(0, metrics.InferenceMs);
        Assert.Equal(0, metrics.LastInferenceMs);
        Assert.Equal(0, metrics.FramesProcessed);
        Assert.Empty(metrics.LastDetections);
        Assert.False(metrics.ModelReady);
    }

    [Fact]
    public void Record_FirstSample_EmaEqualsSample()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        metrics.Record(None, 100);

        Assert.Equal(100, metrics.InferenceMs, 10);
        Assert.Equal(100, metrics.LastInferenceMs, 10);
    }

    [Fact]
    public void Record_SecondSample_AppliesAlphaOfZeroPointTwo()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        metrics.Record(None, 100);
        metrics.Record(None, 200);

        Assert.Equal(120, metrics.InferenceMs, 10); // 0.2 * 200 + 0.8 * 100
        Assert.Equal(200, metrics.LastInferenceMs, 10);
    }

    [Fact]
    public void Record_ThirdSample_ContinuesEma()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        metrics.Record(None, 100);
        metrics.Record(None, 200);
        metrics.Record(None, 50);

        Assert.Equal((0.2 * 50) + (0.8 * 120), metrics.InferenceMs, 10);
    }

    [Fact]
    public void Record_ConstantSamples_EmaStaysConstant()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        for (int i = 0; i < 10; i++)
        {
            metrics.Record(None, 45);
        }

        Assert.Equal(45, metrics.InferenceMs, 10);
    }

    [Fact]
    public void Record_TwoFrames_IncrementsFramesProcessedAndMarksModelReady()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        metrics.Record(None, 10);
        metrics.Record(None, 10);

        Assert.Equal(2, metrics.FramesProcessed);
        Assert.True(metrics.ModelReady);
    }

    [Fact]
    public void MarkModelReady_WithoutRecord_SetsReadyOnly()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        metrics.MarkModelReady();

        Assert.True(metrics.ModelReady);
        Assert.Equal(0, metrics.FramesProcessed);
    }

    [Fact]
    public void Record_FiveFramesQuarterSecondApart_FpsIsFive()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);

        // Records at t = 0, 250, 500, 750, 1000 ms; the fifth closes the 1 s window with 5 frames.
        for (int i = 0; i < 5; i++)
        {
            metrics.Record(None, 10);
            if (i < 4)
            {
                time.Advance(TimeSpan.FromMilliseconds(250));
            }
        }

        Assert.Equal(5, metrics.ProcessedFps, 6);
    }

    [Fact]
    public void Record_WindowNotYetElapsed_FpsStaysZero()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);

        metrics.Record(None, 10);
        time.Advance(TimeSpan.FromMilliseconds(500));
        metrics.Record(None, 10);

        Assert.Equal(0, metrics.ProcessedFps);
    }

    [Fact]
    public void Record_WindowClosesLate_FpsUsesActualElapsedTime()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);

        metrics.Record(None, 10);
        time.Advance(TimeSpan.FromMilliseconds(1500));
        metrics.Record(None, 10); // 2 frames over 1.5 s

        Assert.Equal(2 / 1.5, metrics.ProcessedFps, 6);
    }

    [Fact]
    public void ProcessedFps_NoFramesForTwoWindows_DecaysToZero()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);
        metrics.Record(None, 10);
        time.Advance(TimeSpan.FromSeconds(1));
        metrics.Record(None, 10);
        Assert.True(metrics.ProcessedFps > 0);

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(0, metrics.ProcessedFps);
    }

    [Fact]
    public void Record_DetectionsList_IsSnapshotUnaffectedByLaterMutation()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());
        var source = new List<CoreDetection> { Cup() };

        metrics.Record(source, 10);
        source.Clear();

        Assert.Single(metrics.LastDetections);
        Assert.Equal("cup", metrics.LastDetections[0].ClassName);
    }

    [Fact]
    public void Record_NewFrame_ReplacesLastDetections()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());
        metrics.Record([Cup()], 10);

        metrics.Record(None, 10);

        Assert.Empty(metrics.LastDetections);
    }

    [Fact]
    public void Record_NullDetections_Throws()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        Assert.Throws<ArgumentNullException>(() => metrics.Record(null!, 10));
    }

    [Fact]
    public void SinceLastSuccess_NoRecord_IsNull()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        Assert.Null(metrics.SinceLastSuccess);
    }

    [Fact]
    public void SinceLastSuccess_AfterRecord_TracksElapsedTime()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);
        metrics.Record(None, 10);

        time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(3), metrics.SinceLastSuccess);
    }

    [Fact]
    public void SinceLastSuccess_SecondRecord_ResetsTimestamp()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);
        metrics.Record(None, 10);
        time.Advance(TimeSpan.FromSeconds(4));

        metrics.Record(None, 10);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(1), metrics.SinceLastSuccess);
    }

    [Fact]
    public void SinceLastSuccess_OnlyMarkModelReady_StaysNull()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        metrics.MarkModelReady();

        Assert.Null(metrics.SinceLastSuccess);
    }

    [Fact]
    public void SinceLastFrame_NoFrame_IsNull()
    {
        var metrics = new PipelineMetrics(new FakeTimeProvider());

        Assert.Null(metrics.SinceLastFrame);
    }

    [Fact]
    public void SinceLastFrame_AfterMarkFrameSeen_TracksElapsedTimeWithoutSuccess()
    {
        var time = new FakeTimeProvider();
        var metrics = new PipelineMetrics(time);
        metrics.MarkFrameSeen();

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), metrics.SinceLastFrame);
        Assert.Null(metrics.SinceLastSuccess);
    }
}
