using QualityVision.Core.Geometry;

namespace QualityVision.Core.Detection;

/// <summary>
/// One detected object. <paramref name="Box"/> is normalized (0..1) relative to the frame, so it is
/// resolution independent. <paramref name="ClassName"/> is the model's (English, COCO) class name;
/// use <see cref="GermanLabels.Get"/> for display.
/// </summary>
public sealed record Detection(string ClassName, double Confidence, NormalizedRect Box)
{
    /// <summary>Centre of <see cref="Box"/> in normalized coordinates (used for the zone-entry test in M4).</summary>
    public (double X, double Y) Center => (Box.X + (Box.Width / 2), Box.Y + (Box.Height / 2));
}
