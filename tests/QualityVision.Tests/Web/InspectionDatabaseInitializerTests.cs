using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Persistence;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class InspectionDatabaseInitializerTests
{
    // FakeTimeProvider uses UTC as local time zone: the local day is 2026-11-15 UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static InspectionRecord Row(DateTimeOffset at, Verdict verdict) =>
        new() { Timestamp = at, ClassName = "cup", DisplayName = "Tasse", Verdict = verdict.ToString(), Confidence = 0.8 };

    private static async Task<InspectionLog> StartAsync(InMemoryDb db, params InspectionRecord[] rows)
    {
        db.EnsureCreated();
        using (InspectionDbContext ctx = db.CreateDbContext())
        {
            ctx.Inspections.AddRange(rows);
            await ctx.SaveChangesAsync();
        }

        var log = new InspectionLog();
        var init = new InspectionDatabaseInitializer(db, log, new FakeTimeProvider(Now), NullLogger<InspectionDatabaseInitializer>.Instance);
        await init.StartAsync(CancellationToken.None);
        return log;
    }

    [Fact]
    public async Task Start_EmptyDatabase_CreatesSchemaAndSeedsZero()
    {
        using var db = new InMemoryDb(); // no EnsureCreated: the initializer must create the schema
        var log = new InspectionLog();
        var init = new InspectionDatabaseInitializer(db, log, new FakeTimeProvider(Now), NullLogger<InspectionDatabaseInitializer>.Instance);

        await init.StartAsync(CancellationToken.None);

        Assert.Equal(0, log.Counts.Total);
        Assert.Empty(log.Recent());
    }

    [Fact]
    public async Task Start_SeedsOnlyTodaysCountsPerVerdict()
    {
        using var db = new InMemoryDb();

        InspectionLog log = await StartAsync(
            db,
            Row(Now.AddHours(-1), Verdict.Ok),
            Row(Now.AddHours(-2), Verdict.Ok),
            Row(Now.AddHours(-3), Verdict.Nok),
            Row(Now.AddHours(-4), Verdict.Danger),
            Row(Now.AddMinutes(-5), Verdict.Unknown),
            Row(Now.AddDays(-1), Verdict.Ok), // yesterday
            Row(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), Verdict.Nok)); // first instant of tomorrow

        Assert.Equal(new InspectionCounts(2, 1, 1, 1), log.Counts);
    }

    [Fact]
    public async Task Start_SeedsRecentNewestFirstWithNegativeSequence()
    {
        using var db = new InMemoryDb();

        InspectionLog log = await StartAsync(
            db,
            Row(Now.AddHours(-3), Verdict.Ok),
            Row(Now.AddHours(-1), Verdict.Nok),
            Row(Now.AddHours(-2), Verdict.Danger));

        IReadOnlyList<InspectionEvent> recent = log.Recent();
        Assert.Equal([Verdict.Nok, Verdict.Danger, Verdict.Ok], recent.Select(static e => e.Verdict));
        Assert.All(recent, static e => Assert.True(e.Sequence < 0));
        Assert.Equal("Tasse", recent[0].DisplayName);
        Assert.Equal(Now.AddHours(-1), recent[0].Timestamp);
    }

    [Fact]
    public async Task Start_MoreThan50Today_CountsAllButSeedsLast50()
    {
        using var db = new InMemoryDb();
        InspectionRecord[] rows = [.. Enumerable.Range(1, 60).Select(i => Row(Now.AddMinutes(-i), Verdict.Ok))];

        InspectionLog log = await StartAsync(db, rows);

        Assert.Equal(60, log.Counts.Ok);
        Assert.Equal(50, log.Recent(500).Count);
        Assert.Equal(Now.AddMinutes(-1), log.Recent(1)[0].Timestamp);
    }

    [Fact]
    public async Task Start_UnparsableVerdict_IsSkippedInRecent()
    {
        using var db = new InMemoryDb();
        InspectionRecord bad = Row(Now.AddMinutes(-1), Verdict.Ok);
        bad.Verdict = "Bogus";

        InspectionLog log = await StartAsync(db, bad, Row(Now.AddMinutes(-2), Verdict.Ok));

        Assert.Single(log.Recent());
    }

    [Fact]
    public async Task Start_DoesNotRaiseEventAdded()
    {
        using var db = new InMemoryDb();
        db.EnsureCreated();
        var log = new InspectionLog();
        int raised = 0;
        log.EventAdded += _ => raised++;
        var init = new InspectionDatabaseInitializer(db, log, new FakeTimeProvider(Now), NullLogger<InspectionDatabaseInitializer>.Instance);

        await init.StartAsync(CancellationToken.None);

        Assert.Equal(0, raised);
    }
}
