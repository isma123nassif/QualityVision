using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class InspectionLogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

    private static InspectionEvent Event(long sequence, Verdict verdict = Verdict.Ok, string cls = "cup") =>
        new(sequence, T0.AddSeconds(sequence), cls, cls, verdict, 0.9);

    [Fact]
    public void Counts_NewLog_IsZeroWithZeroScrapRate()
    {
        var log = new InspectionLog();

        Assert.Equal(0, log.Counts.Total);
        Assert.Equal(0, log.Counts.ScrapRate);
        Assert.Empty(log.Recent());
    }

    [Fact]
    public void Counts_MixedVerdicts_AreTallied()
    {
        var log = new InspectionLog();
        log.Add(Event(1, Verdict.Ok));
        log.Add(Event(2, Verdict.Ok));
        log.Add(Event(3, Verdict.Nok));
        log.Add(Event(4, Verdict.Danger));
        log.Add(Event(5, Verdict.Unknown));

        var counts = log.Counts;

        Assert.Equal(2, counts.Ok);
        Assert.Equal(1, counts.Nok);
        Assert.Equal(1, counts.Danger);
        Assert.Equal(1, counts.Unknown);
        Assert.Equal(5, counts.Total);
    }

    [Fact]
    public void ScrapRate_OneOkOneNokPlusDangerAndUnknown_IsHalfBecauseOnlyOkAndNokCount()
    {
        var log = new InspectionLog();
        log.Add(Event(1, Verdict.Ok));
        log.Add(Event(2, Verdict.Nok));
        log.Add(Event(3, Verdict.Danger));
        log.Add(Event(4, Verdict.Unknown));

        Assert.Equal(0.5, log.Counts.ScrapRate, 10);
    }

    [Fact]
    public void InspectionCounts_Direct_TotalIncludesAllButScrapRateOnlyOkAndNok()
    {
        var counts = new InspectionCounts(Ok: 6, Nok: 2, Danger: 1, Unknown: 1);

        Assert.Equal(10, counts.Total);
        Assert.Equal(0.25, counts.ScrapRate, 10); // 2 / (6 + 2)
        Assert.Equal(0, default(InspectionCounts).ScrapRate);
    }

    [Fact]
    public void ScrapRate_OnlyDangerAndUnknown_IsZero()
    {
        var counts = new InspectionCounts(Ok: 0, Nok: 0, Danger: 3, Unknown: 2);

        Assert.Equal(5, counts.Total);
        Assert.Equal(0, counts.ScrapRate);
    }

    [Fact]
    public void Add_ThrowingHandler_DoesNotStopOtherHandlersNorTheAdd()
    {
        var log = new InspectionLog();
        var received = new List<long>();
        log.EventAdded += _ => throw new InvalidOperationException("boom");
        log.EventAdded += e => received.Add(e.Sequence);
        log.EventAdded += _ => throw new InvalidOperationException("boom 2");

        var ex = Record.Exception(() => log.Add(Event(1, Verdict.Nok)));

        Assert.Null(ex);
        Assert.Equal([1L], received);
        Assert.Equal(1, log.Counts.Nok);
        Assert.Single(log.Recent());
    }

    [Fact]
    public void Snapshot_AfterAdds_ReturnsMatchingCountsAndNewestFirstRecent()
    {
        var log = new InspectionLog();
        log.Add(Event(1, Verdict.Ok));
        log.Add(Event(2, Verdict.Nok));
        log.Add(Event(3, Verdict.Danger));

        var (counts, recent) = log.Snapshot(2);

        Assert.Equal(new InspectionCounts(1, 1, 1, 0), counts);
        Assert.Equal([3L, 2L], recent.Select(e => e.Sequence).ToArray());
    }

    [Fact]
    public void Snapshot_NewLog_IsEmpty()
    {
        var (counts, recent) = new InspectionLog().Snapshot(10);

        Assert.Equal(default, counts);
        Assert.Empty(recent);
    }

    [Fact]
    public async Task Snapshot_ConcurrentWriter_CountsAlwaysMatchNewestRecentSequence()
    {
        var log = new InspectionLog();
        var writer = Task.Run(() =>
        {
            for (long i = 1; i <= 2000; i++)
            {
                log.Add(Event(i, Verdict.Ok));
            }
        });

        while (!writer.IsCompleted)
        {
            var (counts, recent) = log.Snapshot(1);

            // Sequences are 1..n in order, so the newest recent event's sequence equals the counted total.
            Assert.Equal(counts.Total, recent.Count == 0 ? 0 : recent[0].Sequence);
        }

        await writer;
    }

    [Fact]
    public void Recent_MultipleEvents_AreNewestFirst()
    {
        var log = new InspectionLog();
        log.Add(Event(1));
        log.Add(Event(2));
        log.Add(Event(3));

        var recent = log.Recent();

        Assert.Equal([3L, 2L, 1L], recent.Select(e => e.Sequence).ToArray());
    }

    [Fact]
    public void Recent_MaxSmallerThanCount_ReturnsOnlyNewest()
    {
        var log = new InspectionLog();
        for (var i = 1; i <= 10; i++)
        {
            log.Add(Event(i));
        }

        var recent = log.Recent(3);

        Assert.Equal([10L, 9L, 8L], recent.Select(e => e.Sequence).ToArray());
    }

    [Fact]
    public void Recent_DefaultMax_Is50()
    {
        var log = new InspectionLog();
        for (var i = 1; i <= 120; i++)
        {
            log.Add(Event(i));
        }

        var recent = log.Recent();

        Assert.Equal(50, recent.Count);
        Assert.Equal(120, recent[0].Sequence);
    }

    [Fact]
    public void Recent_MoreThanBufferCapacity_KeepsLast500ButCountsAll()
    {
        var log = new InspectionLog();
        for (var i = 1; i <= 600; i++)
        {
            log.Add(Event(i, Verdict.Ok));
        }

        var recent = log.Recent(10_000);

        Assert.Equal(500, recent.Count);
        Assert.Equal(600, recent[0].Sequence);
        Assert.Equal(101, recent[^1].Sequence);
        Assert.Equal(600, log.Counts.Total);
    }

    [Fact]
    public void Add_Event_RaisesEventAddedWithThatEvent()
    {
        var log = new InspectionLog();
        var received = new List<InspectionEvent>();
        log.EventAdded += received.Add;
        var e = Event(7, Verdict.Nok);

        log.Add(e);

        Assert.Single(received);
        Assert.Same(e, received[0]);
    }

    [Fact]
    public void Add_HandlerReadsLog_EventAlreadyVisibleAndNoDeadlock()
    {
        var log = new InspectionLog();
        var seenInHandler = -1;
        log.EventAdded += _ => seenInHandler = log.Recent().Count + log.Counts.Total;

        log.Add(Event(1));

        Assert.Equal(2, seenInHandler);
    }

    [Fact]
    public async Task Add_ConcurrentWriters_DoNotLoseCounts()
    {
        var log = new InspectionLog();
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                log.Add(Event((t * 100L) + i, Verdict.Nok));
            }
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(800, log.Counts.Nok);
        Assert.Equal(500, log.Recent(1000).Count);
    }
}
