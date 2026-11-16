using Microsoft.Extensions.Time.Testing;

using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class InspectionLogPerMinuteTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 5, 30, TimeSpan.Zero);

    private static (InspectionLog Log, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(Now);
        return (new InspectionLog(null, time), time);
    }

    private static InspectionEvent At(DateTimeOffset timestamp, Verdict verdict, long sequence = 1) =>
        new(sequence, timestamp, "cup", "cup", verdict, 0.9);

    [Fact]
    public void PerMinute_EmptyLog_ReturnsRequestedMinutesOldestFirstAllZero()
    {
        var (log, _) = Create();

        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(3);

        Assert.Equal(3, buckets.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 3, 0, TimeSpan.Zero), buckets[0].Minute);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 4, 0, TimeSpan.Zero), buckets[1].Minute);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 5, 0, TimeSpan.Zero), buckets[2].Minute);
        Assert.All(buckets, b => Assert.Equal(0, b.Ok + b.Nok + b.Danger + b.Unknown));
    }

    [Fact]
    public void PerMinute_EventsInDifferentMinutes_AreBucketedPerVerdict()
    {
        var (log, _) = Create();
        log.Add(At(Now.AddMinutes(-2).AddSeconds(-10), Verdict.Ok));   // 10:03:20
        log.Add(At(Now.AddMinutes(-2).AddSeconds(5), Verdict.Nok));    // 10:03:35
        log.Add(At(Now.AddSeconds(-20), Verdict.Danger));              // 10:05:10
        log.Add(At(Now, Verdict.Unknown));                             // 10:05:30
        log.Add(At(Now, Verdict.Unknown));

        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(3);

        Assert.Equal(new MinuteBucket(buckets[0].Minute, 1, 1, 0, 0), buckets[0]);
        Assert.Equal(new MinuteBucket(buckets[1].Minute, 0, 0, 0, 0), buckets[1]);
        Assert.Equal(new MinuteBucket(buckets[2].Minute, 0, 0, 1, 2), buckets[2]);
    }

    [Fact]
    public void PerMinute_EmptyMinuteBetweenEvents_IsIncludedWithZeroCounts()
    {
        var (log, _) = Create();
        log.Add(At(Now.AddMinutes(-4), Verdict.Ok));
        log.Add(At(Now, Verdict.Ok));

        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(5);

        Assert.Equal([1, 0, 0, 0, 1], buckets.Select(b => b.Ok));
    }

    [Fact]
    public void PerMinute_EventsOutsideWindow_AreIgnored()
    {
        var (log, _) = Create();
        log.Add(At(new DateTimeOffset(2026, 11, 15, 10, 2, 59, TimeSpan.Zero), Verdict.Ok));  // one minute too old
        log.Add(At(new DateTimeOffset(2026, 11, 15, 10, 6, 0, TimeSpan.Zero), Verdict.Ok));   // future minute

        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(3);

        Assert.All(buckets, b => Assert.Equal(0, b.Ok));
    }

    [Fact]
    public void PerMinute_WindowEdges_OldestMinuteStartAndCurrentMinuteEndAreIncluded()
    {
        var (log, _) = Create();
        log.Add(At(new DateTimeOffset(2026, 11, 15, 10, 3, 0, TimeSpan.Zero), Verdict.Nok));
        log.Add(At(new DateTimeOffset(2026, 11, 15, 10, 5, 59, 999, TimeSpan.Zero), Verdict.Ok));

        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(3);

        Assert.Equal(1, buckets[0].Nok);
        Assert.Equal(1, buckets[2].Ok);
    }

    [Fact]
    public void PerMinute_ClockAdvances_WindowSlides()
    {
        var (log, time) = Create();
        log.Add(At(Now, Verdict.Ok));

        time.Advance(TimeSpan.FromMinutes(2));
        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(3);

        Assert.Equal(new DateTimeOffset(2026, 11, 15, 10, 5, 0, TimeSpan.Zero), buckets[0].Minute);
        Assert.Equal(1, buckets[0].Ok);
        Assert.Equal(0, buckets[2].Ok);
    }

    [Fact]
    public void PerMinute_OffsetTimestamps_AreBucketedInUtc()
    {
        var (log, _) = Create();
        log.Add(At(new DateTimeOffset(2026, 11, 15, 12, 5, 10, TimeSpan.FromHours(2)), Verdict.Ok)); // 10:05:10 UTC

        IReadOnlyList<MinuteBucket> buckets = log.PerMinute(1);

        Assert.Equal(1, buckets[0].Ok);
    }

    [Fact]
    public void PerMinute_ZeroOrNegativeMinutes_ReturnsEmpty()
    {
        var (log, _) = Create();
        log.Add(At(Now, Verdict.Ok));

        Assert.Empty(log.PerMinute(0));
        Assert.Empty(log.PerMinute(-5));
    }

    [Fact]
    public void PerMinute_IgnoreVerdict_IsNotCounted()
    {
        var (log, _) = Create();
        log.Add(At(Now, Verdict.Ignore));

        MinuteBucket bucket = Assert.Single(log.PerMinute(1));

        Assert.Equal(0, bucket.Ok + bucket.Nok + bucket.Danger + bucket.Unknown);
    }
}
