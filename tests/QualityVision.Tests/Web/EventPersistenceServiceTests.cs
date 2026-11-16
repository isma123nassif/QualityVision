using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Persistence;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class EventPersistenceServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static InspectionEvent Event(long sequence, Verdict verdict = Verdict.Ok) =>
        new(sequence, T0.AddSeconds(sequence), "cup", "Tasse", verdict, 0.75);

    [Fact]
    public async Task Events_AreWrittenWithinBatchWindow()
    {
        using var db = new InMemoryDb();
        db.EnsureCreated();
        var log = new InspectionLog();
        using var service = new EventPersistenceService(log, db, NullLogger<EventPersistenceService>.Instance);
        await service.StartAsync(CancellationToken.None);

        log.Add(Event(1, Verdict.Nok));
        log.Add(Event(2, Verdict.Danger));

        List<InspectionRecord> rows = await WaitForRowsAsync(db, 2);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, rows.Count);
        InspectionRecord first = rows[0];
        Assert.Equal(T0.AddSeconds(1), first.Timestamp);
        Assert.Equal("cup", first.ClassName);
        Assert.Equal("Tasse", first.DisplayName);
        Assert.Equal("Nok", first.Verdict);
        Assert.Equal(0.75, first.Confidence);
        Assert.Equal("Danger", rows[1].Verdict);
    }

    [Fact]
    public async Task Stop_FlushesPendingEvents()
    {
        using var db = new InMemoryDb();
        db.EnsureCreated();
        var log = new InspectionLog();
        using var service = new EventPersistenceService(log, db, NullLogger<EventPersistenceService>.Instance);
        await service.StartAsync(CancellationToken.None);

        for (int i = 1; i <= 120; i++)
        {
            log.Add(Event(i));
        }

        await service.StopAsync(CancellationToken.None);

        using InspectionDbContext ctx = db.CreateDbContext();
        Assert.Equal(120, await ctx.Inspections.CountAsync());
    }

    [Fact]
    public async Task DatabaseFailure_DoesNotStopTheService()
    {
        using var db = new InMemoryDb(); // schema deliberately not created: every insert fails
        var log = new InspectionLog();
        using var service = new EventPersistenceService(log, db, NullLogger<EventPersistenceService>.Instance);
        await service.StartAsync(CancellationToken.None);

        log.Add(Event(1));
        await Task.Delay(1500);

        Assert.False(service.ExecuteTask!.IsCompleted);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Seed_DoesNotPersistSeededEvents()
    {
        using var db = new InMemoryDb();
        db.EnsureCreated();
        var log = new InspectionLog();
        using var service = new EventPersistenceService(log, db, NullLogger<EventPersistenceService>.Instance);
        await service.StartAsync(CancellationToken.None);

        log.Seed(new InspectionCounts(1, 0, 0, 0), [Event(1)]);
        await service.StopAsync(CancellationToken.None);

        using InspectionDbContext ctx = db.CreateDbContext();
        Assert.Empty(ctx.Inspections);
    }

    private static async Task<List<InspectionRecord>> WaitForRowsAsync(InMemoryDb db, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using InspectionDbContext ctx = db.CreateDbContext();
            List<InspectionRecord> rows = await ctx.Inspections.OrderBy(static r => r.Timestamp).ToListAsync();
            if (rows.Count >= expected || DateTime.UtcNow > deadline)
            {
                return rows;
            }

            await Task.Delay(50);
        }
    }
}
