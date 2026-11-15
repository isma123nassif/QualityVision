namespace QualityVision.Core.Rules;

/// <summary>Inspection result for one detected object, assigned by <see cref="RulesEngine"/>.</summary>
public enum Verdict
{
    /// <summary>Good part ("i.O.").</summary>
    Ok,

    /// <summary>Defective part ("n.i.O.").</summary>
    Nok,

    /// <summary>Dangerous object in the inspection zone.</summary>
    Danger,

    /// <summary>Class not covered by the rules.</summary>
    Unknown,

    /// <summary>Not judged and not counted (e.g. the operator's hands/person).</summary>
    Ignore,
}
