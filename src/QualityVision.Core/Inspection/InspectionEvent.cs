using QualityVision.Core.Rules;

namespace QualityVision.Core.Inspection;

/// <summary>One counted inspection: an object entered the Prüfzone and received a verdict.</summary>
/// <param name="Sequence">Monotonic per app run, assigned by the caller.</param>
/// <param name="Timestamp">Wall-clock time from <see cref="TimeProvider"/>, taken by the caller.</param>
/// <param name="ClassName">COCO class name, e.g. "cup".</param>
/// <param name="DisplayName">German display name (<c>GermanLabels.Get</c>).</param>
/// <param name="Verdict">The verdict assigned by the rules engine.</param>
/// <param name="Confidence">Detector confidence (0..1).</param>
public sealed record InspectionEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string ClassName,
    string DisplayName,
    Verdict Verdict,
    double Confidence);
