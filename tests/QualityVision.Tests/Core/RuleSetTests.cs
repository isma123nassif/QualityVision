using QualityVision.Core.Rules;

namespace QualityVision.Tests.Core;

public sealed class RuleSetTests
{
    [Theory]
    [InlineData("cup")]
    [InlineData("CUP")]
    [InlineData("Cup")]
    public void VerdictFor_DifferentCasing_FindsRule(string query)
    {
        var rules = new RuleSet([new("cup", Verdict.Nok)]);

        Assert.Equal(Verdict.Nok, rules.VerdictFor(query));
    }

    [Fact]
    public void VerdictFor_UnknownClass_ReturnsUnknown()
    {
        var rules = new RuleSet([new("cup", Verdict.Nok)]);

        Assert.Equal(Verdict.Unknown, rules.VerdictFor("banana"));
    }

    [Fact]
    public void VerdictFor_EmptyRuleSet_ReturnsUnknown()
    {
        Assert.Equal(Verdict.Unknown, RuleSet.Empty.VerdictFor("cup"));
        Assert.Equal(Verdict.Unknown, new RuleSet([]).VerdictFor("cup"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new RuleSet([new(name, Verdict.Ok)]));
    }

    [Fact]
    public void Constructor_DuplicateNamesIgnoringCase_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RuleSet([new("cup", Verdict.Ok), new("CUP", Verdict.Nok)]));
    }

    [Fact]
    public void Constructor_SourceMutatedAfterwards_DoesNotAffectRuleSet()
    {
        var source = new Dictionary<string, Verdict> { ["cup"] = Verdict.Nok };
        var rules = new RuleSet(source);

        source["cup"] = Verdict.Ok;
        source["bottle"] = Verdict.Ok;

        Assert.Equal(Verdict.Nok, rules.VerdictFor("cup"));
        Assert.Equal(Verdict.Unknown, rules.VerdictFor("bottle"));
        Assert.Single(rules.Rules);
    }

    [Fact]
    public void Rules_ExposesAllConfiguredEntries()
    {
        var rules = new RuleSet([new("cup", Verdict.Nok), new("bottle", Verdict.Ok)]);

        Assert.Equal(2, rules.Rules.Count);
        Assert.Equal(Verdict.Ok, rules.Rules["BOTTLE"]);
    }

    [Fact]
    public void Empty_HasNoRules()
    {
        Assert.Empty(RuleSet.Empty.Rules);
    }
}
