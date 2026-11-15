using QualityVision.Core.Detection;
using QualityVision.Web.Pipeline;

namespace QualityVision.Web.Endpoints;

/// <summary>Pipeline diagnostics endpoints.</summary>
public static class PipelineEndpoints
{
    public static IEndpointRouteBuilder MapPipelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/pipeline/metrics", GetMetrics);
        return endpoints;
    }

    private static IResult GetMetrics(IPipelineMetrics metrics)
    {
        var detections = metrics.LastDetections.Select(static d => new
        {
            className = d.ClassName,
            label = GermanLabels.Get(d.ClassName),
            confidence = d.Confidence,
            box = new { x = d.Box.X, y = d.Box.Y, width = d.Box.Width, height = d.Box.Height },
        }).ToArray();

        return Results.Ok(new
        {
            processedFps = metrics.ProcessedFps,
            inferenceMs = metrics.InferenceMs,
            lastInferenceMs = metrics.LastInferenceMs,
            framesProcessed = metrics.FramesProcessed,
            modelReady = metrics.ModelReady,
            detections,
        });
    }
}
