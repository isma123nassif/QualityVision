using System.ComponentModel.DataAnnotations;

using QualityVision.Core.Geometry;

namespace QualityVision.Web.Pipeline;

/// <summary>Inspection settings (section "Inspection"). Validated at startup via data annotations.</summary>
public sealed class InspectionOptions : IValidatableObject
{
    public const string SectionName = "Inspection";

    /// <summary>The Prüfzone in normalized image coordinates.</summary>
    public NormalizedRect Zone { get; set; } = new(0.3, 0.2, 0.4, 0.6);

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
