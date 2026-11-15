using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;

namespace QualityVision.Tests.Core;

public sealed class InspectionVerdictTests
{
    [Theory]
    [InlineData(Verdict.Danger, 4)]
    [InlineData(Verdict.Nok, 3)]
    [InlineData(Verdict.Unknown, 2)]
    [InlineData(Verdict.Ok, 1)]
    [InlineData(Verdict.Ignore, 0)]
    public void Rank_EachVerdict_ReturnsDocumentedRank(Verdict verdict, int expected)
    {
        Assert.Equal(expected, VerdictSeverity.Rank(verdict));
    }

    [Fact]
    public void Rank_Ordering_DangerGreaterThanNokGreaterThanUnknownGreaterThanOkGreaterThanIgnore()
    {
        Assert.True(VerdictSeverity.Rank(Verdict.Danger) > VerdictSeverity.Rank(Verdict.Nok));
        Assert.True(VerdictSeverity.Rank(Verdict.Nok) > VerdictSeverity.Rank(Verdict.Unknown));
        Assert.True(VerdictSeverity.Rank(Verdict.Unknown) > VerdictSeverity.Rank(Verdict.Ok));
        Assert.True(VerdictSeverity.Rank(Verdict.Ok) > VerdictSeverity.Rank(Verdict.Ignore));
    }

    [Theory]
    [InlineData(Verdict.Ok, "i.O.")]
    [InlineData(Verdict.Nok, "n.i.O.")]
    [InlineData(Verdict.Danger, "GEFAHR")]
    [InlineData(Verdict.Unknown, "Unbekannt")]
    [InlineData(Verdict.Ignore, "Ignoriert")]
    public void Get_EachVerdict_ReturnsGermanLabel(Verdict verdict, string expected)
    {
        Assert.Equal(expected, VerdictLabels.Get(verdict));
    }
}
