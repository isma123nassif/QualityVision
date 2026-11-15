using QualityVision.Core.Geometry;

namespace QualityVision.Tests.Core;

public sealed class NormalizedRectTests
{
    private static readonly NormalizedRect Rect = new(0.25, 0.5, 0.5, 0.25); // x 0.25..0.75, y 0.5..0.75

    [Theory]
    [InlineData(0.5, 0.6, true)]    // inside
    [InlineData(0.25, 0.5, true)]   // top-left corner
    [InlineData(0.75, 0.75, true)]  // bottom-right corner
    [InlineData(0.25, 0.6, true)]   // left edge
    [InlineData(0.75, 0.6, true)]   // right edge
    [InlineData(0.5, 0.5, true)]    // top edge
    [InlineData(0.5, 0.75, true)]   // bottom edge
    [InlineData(0.24, 0.6, false)]  // just left
    [InlineData(0.76, 0.6, false)]  // just right
    [InlineData(0.5, 0.49, false)]  // just above
    [InlineData(0.5, 0.76, false)]  // just below
    [InlineData(0.0, 0.0, false)]
    public void Contains_Point_RespectsInclusiveEdges(double x, double y, bool expected)
    {
        Assert.Equal(expected, Rect.Contains(x, y));
    }

    [Theory]
    [InlineData(0.3, 0.2, 0.4, 0.6, true)]
    [InlineData(0.0, 0.0, 1.0, 1.0, true)]
    [InlineData(0.5, 0.5, 0.5, 0.5, true)]
    [InlineData(-0.1, 0.0, 0.5, 0.5, false)]
    [InlineData(0.0, -0.1, 0.5, 0.5, false)]
    [InlineData(0.0, 0.0, 0.0, 0.5, false)]
    [InlineData(0.0, 0.0, 0.5, 0.0, false)]
    [InlineData(0.0, 0.0, -0.5, 0.5, false)]
    [InlineData(0.6, 0.0, 0.5, 0.5, false)]
    [InlineData(0.0, 0.6, 0.5, 0.5, false)]
    [InlineData(0.0, 0.0, 1.5, 0.5, false)]
    [InlineData(double.NaN, 0.0, 0.5, 0.5, false)]
    public void IsValid_Rect_MatchesContract(double x, double y, double w, double h, bool expected)
    {
        Assert.Equal(expected, new NormalizedRect(x, y, w, h).IsValid);
    }
}
