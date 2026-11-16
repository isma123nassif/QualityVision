using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
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

    private static IResult GetMetrics(IPipelineMetrics metrics, IOptionsMonitor<InspectionOptions> inspectionOptions)
    {
        var zone = inspectionOptions.CurrentValue.Zone;
        var detections = metrics.LastDetections.Select(c => new
        {
            className = c.Detection.ClassName,
            label = GermanLabels.Get(c.Detection.ClassName),
            confidence = c.Detection.Confidence,
            verdict = c.Verdict.ToString(),
            verdictLabel = VerdictLabels.Get(c.Verdict),
            inZone = c.Verdict != Verdict.Ignore && InspectionZone.IsInside(c.Detection, zone),
            box = new { x = c.Detection.Box.X, y = c.Detection.Box.Y, width = c.Detection.Box.Width, height = c.Detection.Box.Height },
        }).ToArray();

        return Results.Ok(new
        {
            processedFps = metrics.ProcessedFps,
            inferenceMs = metrics.InferenceMs,
            lastInferenceMs = metrics.LastInferenceMs,
            framesProcessed = metrics.FramesProcessed,
            modelReady = metrics.ModelReady,
            device = metrics.Device,
            model = metrics.Model,
            detections,
        });
    }
}
