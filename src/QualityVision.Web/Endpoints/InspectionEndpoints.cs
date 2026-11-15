using QualityVision.Core.Inspection;
using QualityVision.Web.Pipeline;

namespace QualityVision.Web.Endpoints;

/// <summary>Inspection results: counters since app start and the latest counted events.</summary>
public static class InspectionEndpoints
{
    private const int RecentCount = 20;

    public static IEndpointRouteBuilder MapInspectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/inspection/summary", GetSummary);
        return endpoints;
    }

    private static IResult GetSummary(IInspectionLog inspectionLog)
    {
        (InspectionCounts counts, IReadOnlyList<InspectionEvent> events) = inspectionLog.Snapshot(RecentCount);
        var recent = events.Select(static e => new
        {
            sequence = e.Sequence,
            timestamp = e.Timestamp,
            className = e.ClassName,
            displayName = e.DisplayName,
            verdict = e.Verdict.ToString(),
            verdictLabel = VerdictLabels.Get(e.Verdict),
            confidence = e.Confidence,
        }).ToArray();

        return Results.Ok(new
        {
            counts = new
            {
                ok = counts.Ok,
                nok = counts.Nok,
                danger = counts.Danger,
                unknown = counts.Unknown,
                total = counts.Total,
                scrapRate = counts.ScrapRate,
            },
            recent,
        });
    }
}
