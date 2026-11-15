using QualityVision.Core.Geometry;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Core;

public sealed class DetectionTests
{
    [Fact]
    public void Center_TypicalBox_IsBoxMidpoint()
    {
        var detection = new CoreDetection("cup", 0.9, new NormalizedRect(0.2, 0.4, 0.2, 0.1));

        (double x, double y) = detection.Center;

        Assert.Equal(0.3, x, 10);
        Assert.Equal(0.45, y, 10);
    }

    [Fact]
    public void Center_FullFrameBox_IsFrameCentre()
    {
        var detection = new CoreDetection("cup", 0.9, new NormalizedRect(0, 0, 1, 1));

        Assert.Equal((0.5, 0.5), detection.Center);
    }

    [Fact]
    public void Center_BoxAtOrigin_IsHalfSize()
    {
        var detection = new CoreDetection("cup", 0.9, new NormalizedRect(0, 0, 0.5, 0.25));

        Assert.Equal((0.25, 0.125), detection.Center);
    }
}
