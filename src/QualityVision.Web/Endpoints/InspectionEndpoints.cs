using System.Globalization;
using System.Text;

using Microsoft.EntityFrameworkCore;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Persistence;
using QualityVision.Web.Pipeline;

namespace QualityVision.Web.Endpoints;

/// <summary>Inspection results: counters since app start and the latest counted events.</summary>
public static class InspectionEndpoints
{
    private const int RecentCount = 20;

    public static IEndpointRouteBuilder MapInspectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/inspection/summary", GetSummary);
        endpoints.MapGet("/api/inspection/export.csv", ExportCsvAsync);
        return endpoints;
    }

    private static async Task<IResult> ExportCsvAsync(
        HttpContext context,
        IDbContextFactory<InspectionDbContext> dbFactory,
        TimeProvider time,
        string? from,
        string? to,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        if (!TryParseDay(from, today, out DateOnly fromDay) || !TryParseDay(to, fromDay > today ? fromDay : today, out DateOnly toDay))
        {
            return Results.BadRequest("Ungültiges Datum, erwartet wird yyyy-MM-dd.");
        }

        if (toDay < fromDay)
        {
            return Results.BadRequest("'to' darf nicht vor 'from' liegen.");
        }

        (DateTimeOffset start, DateTimeOffset end) = LocalDay.Range(fromDay, toDay, time);
        TimeZoneInfo zone = time.LocalTimeZone;

        context.Response.ContentType = "text/csv; charset=utf-8";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"pruefungen-{fromDay:yyyyMMdd}.csv\"";

        await using InspectionDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        // UTF-8 BOM so that Excel detects the encoding.
        await using var writer = new StreamWriter(context.Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteAsync(InspectionCsv.Header + "\r\n");

        await foreach (InspectionRecord r in db.Inspections
            .AsNoTracking()
            .Where(x => x.Timestamp >= start && x.Timestamp < end)
            .OrderBy(static x => x.Timestamp)
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (!Enum.TryParse(r.Verdict, out Verdict verdict))
            {
                continue;
            }

            DateTimeOffset local = TimeZoneInfo.ConvertTime(r.Timestamp, zone);
            await writer.WriteAsync(InspectionCsv.FormatRow(local, r.ClassName, r.DisplayName, verdict, r.Confidence) + "\r\n");
        }

        await writer.FlushAsync(cancellationToken);
        return Results.Empty;
    }

    private static bool TryParseDay(string? text, DateOnly fallback, out DateOnly day)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            day = fallback;
            return true;
        }

        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
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
