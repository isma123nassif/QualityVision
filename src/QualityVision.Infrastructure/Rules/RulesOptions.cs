namespace QualityVision.Infrastructure.Rules;

/// <summary>
/// Rules from <c>rules.json</c> (section "Rules"): COCO class name → verdict name. Verdicts are bound as strings and
/// parsed by <see cref="OptionsRuleSetProvider"/>, so a typo is handled by our code, not inside the binder (ADR-002).
/// </summary>
public sealed class RulesOptions
{
    public const string SectionName = "Rules";

    /// <summary>Class name → verdict string ("Ok", "Nok", "Danger", "Unknown", "Ignore"; case-insensitive).</summary>
    public Dictionary<string, string> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
