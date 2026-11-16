using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Healthy if an inference succeeded within the last 5 s, or if no frames arrive (camera problem, not the model).
/// Degraded if frames are processed but inference keeps failing, or before the first success.
/// </summary>
public sealed class ModelHealthCheck(IPipelineMetrics metrics) : IHealthCheck
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        string info = string.IsNullOrEmpty(metrics.Device) ? string.Empty : $" ({metrics.Device} · {metrics.Model})";
        TimeSpan? sinceSuccess = metrics.SinceLastSuccess;
        TimeSpan? sinceFrame = metrics.SinceLastFrame;

        HealthCheckResult result;
        if (sinceSuccess is { } s && s <= Window)
        {
            result = HealthCheckResult.Healthy($"Inferenz erfolgreich.{info}");
        }
        else if (sinceFrame is null || sinceFrame > Window)
        {
            // No frames arriving: the camera is the issue, not the model (unless warm-up never completed).
            result = metrics.ModelReady
                ? HealthCheckResult.Healthy($"Keine Bilder{info}")
                : HealthCheckResult.Degraded($"Modell noch nicht bereit.{info}");
        }
        else
        {
            result = HealthCheckResult.Degraded(sinceSuccess is null
                ? $"Noch keine erfolgreiche Inferenz.{info}"
                : $"Bilder werden verarbeitet, aber seit über 5 s keine erfolgreiche Inferenz.{info}");
        }

        return Task.FromResult(result);
    }
}
