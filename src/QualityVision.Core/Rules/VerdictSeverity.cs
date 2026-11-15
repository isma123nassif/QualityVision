namespace QualityVision.Core.Rules;

/// <summary>Severity order of verdicts: Danger &gt; Nok &gt; Unknown &gt; Ok &gt; Ignore (used for the zone colour).</summary>
public static class VerdictSeverity
{
    /// <summary>Higher is more severe: Danger 4, Nok 3, Unknown 2, Ok 1, Ignore 0.</summary>
    /// <param name="v">The verdict to rank.</param>
    public static int Rank(Verdict v) => v switch
    {
        Verdict.Danger => 4,
        Verdict.Nok => 3,
        Verdict.Unknown => 2,
        Verdict.Ok => 1,
        Verdict.Ignore => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(v), v, "Undefined verdict."),
    };
}
