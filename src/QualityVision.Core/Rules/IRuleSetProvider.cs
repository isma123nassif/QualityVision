namespace QualityVision.Core.Rules;

/// <summary>
/// Port for the currently active rules. Read <see cref="Current"/> once per frame so every detection of that frame
/// is judged against the same snapshot. Implemented in Infrastructure (hot reload from <c>rules.json</c>).
/// </summary>
public interface IRuleSetProvider
{
    /// <summary>The latest valid rule set (never null).</summary>
    RuleSet Current { get; }
}
