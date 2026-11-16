using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class InspectionLogSeedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static InspectionEvent Event(long sequence, Verdict verdict = Verdict.Ok) =>
        new(sequence, T0.AddSeconds(sequence), "cup", "cup", verdict, 0.9);

    [Fact]
    public void Seed_SetsCountsAndRecentNewestFirst()
    {
        var log = new InspectionLog();

        log.Seed(new InspectionCounts(5, 2, 1, 0), [Event(3), Event(2), Event(1)]);

        Assert.Equal(new InspectionCounts(5, 2, 1, 0), log.Counts);
        Assert.Equal([3L, 2L, 1L], log.Recent().Select(static e => e.Sequence));
    }

    [Fact]
    public void Seed_ReplacesPreviousState()
    {
        var log = new InspectionLog();
        log.Add(Event(1, Verdict.Nok));

        log.Seed(new InspectionCounts(1, 0, 0, 0), [Event(9)]);

        Assert.Equal(new InspectionCounts(1, 0, 0, 0), log.Counts);
        Assert.Equal(9, Assert.Single(log.Recent()).Sequence);
    }

    [Fact]
    public void Add_AfterSeed_ContinuesCountsAndPutsNewEventFirst()
    {
        var log = new InspectionLog();
        log.Seed(new InspectionCounts(1, 0, 0, 0), [Event(-1)]);

        log.Add(Event(1, Verdict.Nok));

        Assert.Equal(new InspectionCounts(1, 1, 0, 0), log.Counts);
        Assert.Equal([1L, -1L], log.Recent().Select(static e => e.Sequence));
    }

    [Fact]
    public void Seed_RaisesNoEventAdded()
    {
        var log = new InspectionLog();
        int raised = 0;
        log.EventAdded += _ => raised++;

        log.Seed(new InspectionCounts(1, 0, 0, 0), [Event(1)]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Seed_MoreThanCapacity_KeepsNewest()
    {
        var log = new InspectionLog();
        InspectionEvent[] recent = [.. Enumerable.Range(0, InspectionLog.Capacity + 10).Select(i => Event(1000 - i))];

        log.Seed(default, recent);

        Assert.Equal(InspectionLog.Capacity, log.Recent(int.MaxValue).Count);
        Assert.Equal(1000, log.Recent(1)[0].Sequence);
    }

    [Fact]
    public void Seed_NullRecent_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InspectionLog().Seed(default, null!));
}
