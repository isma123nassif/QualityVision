using QualityVision.Core.Geometry;
using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Core;

public sealed class InspectionZoneTests
{
    private static readonly NormalizedRect Zone = new(0.25, 0.25, 0.5, 0.5);

    private static CoreDetection At(double cx, double cy, double size = 0.1) =>
        new("cup", 0.9, new NormalizedRect(cx - (size / 2), cy - (size / 2), size, size));

    [Theory]
    [InlineData(0.5, 0.5, true)]
    [InlineData(0.1, 0.5, false)]
    [InlineData(0.9, 0.5, false)]
    [InlineData(0.5, 0.1, false)]
    [InlineData(0.5, 0.9, false)]
    public void IsInside_CentreInsideOrOutside_ReturnsExpected(double cx, double cy, bool expected)
    {
        Assert.Equal(expected, InspectionZone.IsInside(At(cx, cy), Zone));
    }

    [Fact]
    public void IsInside_CentreExactlyOnEdges_CountsAsInside()
    {
        // Binary-exact values so the centre lands exactly on the zone edge.
        var left = new CoreDetection("cup", 0.9, new NormalizedRect(0.125, 0.5, 0.25, 0.25));     // centre (0.25, 0.625)
        var right = new CoreDetection("cup", 0.9, new NormalizedRect(0.625, 0.5, 0.25, 0.25));    // centre (0.75, 0.625)
        var top = new CoreDetection("cup", 0.9, new NormalizedRect(0.375, 0.125, 0.25, 0.25));    // centre (0.5, 0.25)
        var bottom = new CoreDetection("cup", 0.9, new NormalizedRect(0.375, 0.625, 0.25, 0.25)); // centre (0.5, 0.75)

        Assert.True(InspectionZone.IsInside(left, Zone));
        Assert.True(InspectionZone.IsInside(right, Zone));
        Assert.True(InspectionZone.IsInside(top, Zone));
        Assert.True(InspectionZone.IsInside(bottom, Zone));
    }

    [Fact]
    public void IsInside_BoxOverlapsButCentreOutside_ReturnsFalse()
    {
        // Box spans 0.1..0.3 horizontally (overlaps the zone from 0.25) but its centre 0.2 is outside.
        var detection = new CoreDetection("cup", 0.9, new NormalizedRect(0.1, 0.4, 0.2, 0.2));

        Assert.False(InspectionZone.IsInside(detection, Zone));
    }

    [Fact]
    public void FilterInside_MixedDetections_KeepsOnlyInsideAndExcludesIgnore()
    {
        var inside = new ClassifiedDetection(At(0.5, 0.5), Verdict.Nok);
        var outside = new ClassifiedDetection(At(0.05, 0.05), Verdict.Nok);
        var ignored = new ClassifiedDetection(At(0.5, 0.5), Verdict.Ignore);

        var result = InspectionZone.FilterInside([inside, outside, ignored], Zone);

        Assert.Single(result);
        Assert.Same(inside, result[0]);
    }

    [Fact]
    public void FilterInside_Empty_ReturnsEmpty()
    {
        Assert.Empty(InspectionZone.FilterInside([], Zone));
    }
}
