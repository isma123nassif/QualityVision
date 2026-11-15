using QualityVision.Core.Rules;

namespace QualityVision.Core.Inspection;

/// <summary>German short labels for verdicts (i.O. / n.i.O. = in Ordnung / nicht in Ordnung).</summary>
public static class VerdictLabels
{
    /// <summary>German short label for <paramref name="verdict"/>.</summary>
    /// <param name="verdict">The verdict to label.</param>
    public static string Get(Verdict verdict) => verdict switch
    {
        Verdict.Ok => "i.O.",
        Verdict.Nok => "n.i.O.",
        Verdict.Danger => "GEFAHR",
        Verdict.Unknown => "Unbekannt",
        Verdict.Ignore => "Ignoriert",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "Undefined verdict."),
    };
}
