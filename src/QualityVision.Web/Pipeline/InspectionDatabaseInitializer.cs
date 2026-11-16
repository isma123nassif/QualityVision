using Microsoft.EntityFrameworkCore;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Persistence;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Creates the schema (<c>EnsureCreated</c>: single table, no migrations: demo trade-off; production would use
/// migrations) and seeds <see cref="IInspectionLog"/> with today's data. Registered before the pipeline services, so
/// its <c>StartAsync</c> completes before camera and detection start.
/// </summary>
public sealed class InspectionDatabaseInitializer(
    IDbContextFactory<InspectionDbContext> dbFactory,
    IInspectionLog log,
    TimeProvider time,
    ILogger<InspectionDatabaseInitializer> logger) : IHostedService
{
    private const int RecentCount = 50;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using InspectionDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            // WAL: the CSV export's read cursor must not block persistence commits (persisted in the database file).
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);

            (DateTimeOffset from, DateTimeOffset to) = LocalDay.Range(DateOnly.FromDateTime(time.GetLocalNow().DateTime), time);

            // Verdict is stored as text; group in SQL, parse afterwards.
            var grouped = await db.Inspections
                .Where(r => r.Timestamp >= from && r.Timestamp < to)
                .GroupBy(static r => r.Verdict)
                .Select(static g => new { Verdict = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            int Count(Verdict v) => grouped.Where(g => g.Verdict == v.ToString()).Sum(static g => g.Count);
            var counts = new InspectionCounts(Count(Verdict.Ok), Count(Verdict.Nok), Count(Verdict.Danger), Count(Verdict.Unknown));

            List<InspectionRecord> rows = await db.Inspections
                .Where(r => r.Timestamp >= from && r.Timestamp < to)
                .OrderByDescending(static r => r.Timestamp)
                .Take(RecentCount)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var recent = new List<InspectionEvent>(rows.Count);
            foreach (InspectionRecord r in rows)
            {
                if (Enum.TryParse(r.Verdict, out Verdict verdict))
                {
                    // Negative sequence: cannot collide with this run's positive sequence numbers (UI @key).
                    recent.Add(new InspectionEvent(-r.Id, r.Timestamp, r.ClassName, r.DisplayName, verdict, r.Confidence));
                }
            }

            log.Seed(counts, recent);
            logger.LogInformation("Inspection database ready; seeded {Total} inspections of today", counts.Total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Without a database the demo still runs (counters start at zero, nothing is persisted).
            logger.LogError(ex, "Inspection database initialisation failed");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
