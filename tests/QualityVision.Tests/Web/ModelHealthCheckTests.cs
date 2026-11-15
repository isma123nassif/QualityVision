using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class ModelHealthCheckTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly PipelineMetrics _metrics;
    private readonly ModelHealthCheck _check;

    public ModelHealthCheckTests()
    {
        _metrics = new PipelineMetrics(_time);
        _check = new ModelHealthCheck(_metrics);
    }

    private Task<HealthCheckResult> CheckAsync() => _check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

    [Fact]
    public async Task CheckHealthAsync_NothingHappenedYet_IsDegraded()
    {
        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WarmUpDoneButNoFrames_IsHealthyKeineBilder()
    {
        _metrics.MarkModelReady();

        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Keine Bilder", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_RecentSuccess_IsHealthy()
    {
        _metrics.MarkFrameSeen();
        _metrics.Record([], 12);
        _time.Advance(TimeSpan.FromSeconds(3));

        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessExactlyFiveSecondsAgo_IsHealthy()
    {
        _metrics.MarkFrameSeen();
        _metrics.Record([], 12);
        _time.Advance(TimeSpan.FromSeconds(5));

        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_FramesArriveButNoSuccessForOverFiveSeconds_IsDegraded()
    {
        _metrics.MarkFrameSeen();
        _metrics.Record([], 12);
        _time.Advance(TimeSpan.FromSeconds(4));
        _metrics.MarkFrameSeen(); // frames keep coming, inference keeps failing
        _time.Advance(TimeSpan.FromSeconds(2));

        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_FramesArriveButNeverSucceeded_IsDegraded()
    {
        _metrics.MarkModelReady();
        _metrics.MarkFrameSeen();

        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_SuccessLongAgoAndFramesStopped_IsHealthyKeineBilder()
    {
        _metrics.MarkFrameSeen();
        _metrics.Record([], 12);
        _time.Advance(TimeSpan.FromSeconds(30));

        HealthCheckResult result = await CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Keine Bilder", result.Description);
    }

    [Fact]
    public async Task CheckHealthAsync_RecoversAfterNewSuccess_IsHealthy()
    {
        _metrics.MarkFrameSeen();
        _time.Advance(TimeSpan.FromSeconds(1));
        _metrics.MarkModelReady();
        Assert.Equal(HealthStatus.Degraded, (await CheckAsync()).Status);

        _metrics.Record([], 10);

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync()).Status);
    }
}
