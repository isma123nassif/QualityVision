using QualityVision.Core.Geometry;
using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Core;

public sealed class ZoneEntryTrackerTests
{
    private static readonly IReadOnlyList<ClassifiedDetection> Empty = [];

    private static ClassifiedDetection Obj(string name, Verdict verdict = Verdict.Nok, double confidence = 0.9) =>
        new(new CoreDetection(name, confidence, new NormalizedRect(0.4, 0.4, 0.2, 0.2)), verdict);

    /// <summary>Feeds the same in-zone detections for <paramref name="frames"/> frames; returns the entries counted.</summary>
    private static int Feed(ZoneEntryTracker tracker, int frames, params ClassifiedDetection[] inZone)
    {
        var entries = 0;
        for (var i = 0; i < frames; i++)
        {
            entries += tracker.Update(inZone).Count;
        }

        return entries;
    }

    [Fact]
    public void Update_FirstAppearance_CountsOnce()
    {
        var tracker = new ZoneEntryTracker();

        var entries = tracker.Update([Obj("cup")]);

        Assert.Single(entries);
        Assert.Equal("cup", entries[0].Detection.ClassName);
        Assert.Empty(tracker.Update([Obj("cup")]));
    }

    [Fact]
    public void Update_ContinuousPresenceFor50Frames_CountsOnce()
    {
        var tracker = new ZoneEntryTracker();

        Assert.Equal(1, Feed(tracker, 50, Obj("cup")));
    }

    [Fact]
    public void Update_NoDetections_ReturnsEmpty()
    {
        var tracker = new ZoneEntryTracker();

        Assert.Empty(tracker.Update(Empty));
    }

    [Fact]
    public void Update_OneFrameFlickerGap_DoesNotRecount()
    {
        var tracker = new ZoneEntryTracker();

        var total = Feed(tracker, 3, Obj("cup"));
        total += Feed(tracker, 1);
        total += Feed(tracker, 3, Obj("cup"));

        Assert.Equal(1, total);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void Update_GapOfNMinusOne_DoesNotRecount(int n)
    {
        var tracker = new ZoneEntryTracker(n);

        var total = Feed(tracker, 2, Obj("cup"));
        total += Feed(tracker, n - 1);
        total += Feed(tracker, 2, Obj("cup"));

        Assert.Equal(1, total);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void Update_GapOfNThenReappearance_CountsAgain(int n)
    {
        var tracker = new ZoneEntryTracker(n);

        var total = Feed(tracker, 2, Obj("cup"));
        total += Feed(tracker, n);
        total += Feed(tracker, 2, Obj("cup"));

        Assert.Equal(2, total);
    }

    [Fact]
    public void Update_DefaultAbsenceFrames_IsFive()
    {
        var tracker = new ZoneEntryTracker();

        var total = Feed(tracker, 1, Obj("cup"));
        total += Feed(tracker, 4);
        total += Feed(tracker, 1, Obj("cup"));
        Assert.Equal(1, total);

        total += Feed(tracker, 5);
        total += Feed(tracker, 1, Obj("cup"));
        Assert.Equal(2, total);
    }

    [Fact]
    public void Update_PresenceBetweenGaps_ResetsMissCounter()
    {
        var tracker = new ZoneEntryTracker(3);

        // Two misses, seen again (counter reset), two more misses: never 3 consecutive.
        var total = Feed(tracker, 1, Obj("cup"));
        total += Feed(tracker, 2);
        total += Feed(tracker, 1, Obj("cup"));
        total += Feed(tracker, 2);
        total += Feed(tracker, 1, Obj("cup"));

        Assert.Equal(1, total);
    }

    [Fact]
    public void Update_TwoClasses_AreIndependent()
    {
        var tracker = new ZoneEntryTracker(2);

        var first = tracker.Update([Obj("cup")]);
        var second = tracker.Update([Obj("cup"), Obj("bottle", Verdict.Ok)]);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal("bottle", second[0].Detection.ClassName);

        // cup leaves for N frames while bottle stays; cup recounts, bottle does not.
        tracker.Update([Obj("bottle", Verdict.Ok)]);
        tracker.Update([Obj("bottle", Verdict.Ok)]);
        var back = tracker.Update([Obj("cup"), Obj("bottle", Verdict.Ok)]);

        Assert.Single(back);
        Assert.Equal("cup", back[0].Detection.ClassName);
    }

    [Fact]
    public void Update_TwoClassesEnterInSameFrame_BothCount()
    {
        var tracker = new ZoneEntryTracker();

        var entries = tracker.Update([Obj("cup"), Obj("scissors", Verdict.Danger)]);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Update_TwoDetectionsOfSameClass_CountsOnceWithHighestConfidence()
    {
        var tracker = new ZoneEntryTracker();

        var entries = tracker.Update(
            [Obj("cup", confidence: 0.55), Obj("cup", confidence: 0.91), Obj("cup", confidence: 0.70)]);

        Assert.Single(entries);
        Assert.Equal(0.91, entries[0].Detection.Confidence);
    }

    [Fact]
    public void Update_ClassNamesDifferingInCase_AreTheSameClass()
    {
        var tracker = new ZoneEntryTracker();

        var total = Feed(tracker, 1, Obj("cup"));
        total += Feed(tracker, 1, Obj("CUP"));

        Assert.Equal(1, total);
    }

    [Fact]
    public void Update_IgnoreVerdict_NeverCounts()
    {
        var tracker = new ZoneEntryTracker();

        Assert.Equal(0, Feed(tracker, 10, Obj("person", Verdict.Ignore)));
    }

    [Fact]
    public void Update_IgnoreAlongsideOtherClass_OnlyOtherCounts()
    {
        var tracker = new ZoneEntryTracker();

        var entries = tracker.Update([Obj("person", Verdict.Ignore), Obj("cup")]);

        Assert.Single(entries);
        Assert.Equal("cup", entries[0].Detection.ClassName);
    }

    [Fact]
    public void Update_AbsenceFramesOne_RecountsAfterSingleMissedFrame()
    {
        var tracker = new ZoneEntryTracker(1);

        var total = Feed(tracker, 1, Obj("cup"));
        total += Feed(tracker, 1);
        total += Feed(tracker, 1, Obj("cup"));

        Assert.Equal(2, total);
    }

    [Fact]
    public void Update_AbsenceFramesOne_ContinuousPresenceCountsOnce()
    {
        var tracker = new ZoneEntryTracker(1);

        Assert.Equal(1, Feed(tracker, 20, Obj("cup")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_AbsenceFramesBelowOne_Throws(int n)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneEntryTracker(n));
    }

    [Fact]
    public void Reset_WhilePresent_CountsNextAppearanceAgain()
    {
        var tracker = new ZoneEntryTracker();

        var total = Feed(tracker, 5, Obj("cup"));
        tracker.Reset();
        total += Feed(tracker, 1, Obj("cup"));

        Assert.Equal(2, total);
    }

    [Fact]
    public void Reset_ClearsPendingMissCounters()
    {
        var tracker = new ZoneEntryTracker(3);

        Feed(tracker, 1, Obj("cup"));
        Feed(tracker, 2);
        tracker.Reset();

        // After reset the class is Absent: one appearance counts exactly once, no stale state.
        Assert.Equal(1, Feed(tracker, 1, Obj("cup")));
        Assert.Equal(0, Feed(tracker, 5, Obj("cup")));
    }

    [Fact]
    public void Update_TwoObjectsOfSameClassOneLeaves_DoesNotRecount()
    {
        // Documented limitation (ADR-002): the tracker is per class, not per object.
        var tracker = new ZoneEntryTracker();

        var total = Feed(tracker, 1, Obj("cup"), Obj("cup"));
        total += Feed(tracker, 10, Obj("cup"));

        Assert.Equal(1, total);
    }

    [Fact]
    public void Update_SecondObjectOfSameClassEntersWhilePresent_DoesNotCountAgain()
    {
        // Pins the documented limitation: a second cup arriving while a cup is present is not counted.
        var tracker = new ZoneEntryTracker();

        var total = Feed(tracker, 3, Obj("cup"));
        total += Feed(tracker, 3, Obj("cup"), Obj("cup"));

        Assert.Equal(1, total);
    }
}
