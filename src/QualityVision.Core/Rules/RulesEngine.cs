using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Core.Rules;

/// <summary>Pure business rule: same detection and rule set, same verdict. No state, no configuration, no clock.</summary>
public static class RulesEngine
{
    /// <summary>Assigns the verdict for <paramref name="detection"/> by its class name.</summary>
    /// <param name="detection">The detected object.</param>
    /// <param name="rules">The rule snapshot for the current frame.</param>
    public static Verdict Evaluate(CoreDetection detection, RuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(rules);
        return rules.VerdictFor(detection.ClassName);
    }
}
