using System.ComponentModel.DataAnnotations;

using QualityVision.Core.Geometry;

namespace QualityVision.Web.Pipeline;

/// <summary>Inspection settings (section "Inspection"). Validated at startup via data annotations.</summary>
public sealed class InspectionOptions : IValidatableObject
{
    public const string SectionName = "Inspection";

    /// <summary>The Prüfzone in normalized image coordinates.</summary>
    public NormalizedRect Zone { get; set; } = new(0.3, 0.2, 0.4, 0.6);

    /// <summary>
    /// Debounce: consecutive evaluated frames without a class before it can be counted again (ADR-002 §2).
    /// About 0.5 s at 10 FPS.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Inspection:AbsenceFrames must be between 1 and 100.")]
    public int AbsenceFrames { get; set; } = 5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Zone.IsValid)
        {
            yield return new ValidationResult(
                "Inspection:Zone must lie within 0..1 with positive Width/Height and X+Width <= 1, Y+Height <= 1.",
                [nameof(Zone)]);
        }
    }
}
