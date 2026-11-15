using QualityVision.Core.Geometry;
using QualityVision.Core.Rules;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Core.Inspection;

/// <summary>Prüfzone helpers: an object is "in the zone" when the centre of its box lies inside (edges inclusive).</summary>
public static class InspectionZone
{
    /// <summary>True if the box centre of <paramref name="d"/> lies inside <paramref name="zone"/> (edges inclusive).</summary>
    /// <param name="d">The detection.</param>
    /// <param name="zone">The Prüfzone in normalized coordinates.</param>
    public static bool IsInside(CoreDetection d, NormalizedRect zone)
    {
        ArgumentNullException.ThrowIfNull(d);
        (double x, double y) = d.Center;
        return zone.Contains(x, y);
    }

    /// <summary>The detections whose centre is inside <paramref name="zone"/>, excluding <see cref="Verdict.Ignore"/>.</summary>
    /// <param name="detections">This frame's classified detections.</param>
    /// <param name="zone">The Prüfzone in normalized coordinates.</param>
    public static IReadOnlyList<ClassifiedDetection> FilterInside(IEnumerable<ClassifiedDetection> detections, NormalizedRect zone)
    {
        ArgumentNullException.ThrowIfNull(detections);
        return detections
            .Where(c => c.Verdict != Verdict.Ignore && IsInside(c.Detection, zone))
            .ToArray();
    }
}
