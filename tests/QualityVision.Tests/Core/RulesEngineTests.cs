using QualityVision.Core.Geometry;
using QualityVision.Core.Rules;

using CoreDetection = QualityVision.Core.Detection.Detection;

namespace QualityVision.Tests.Core;

public sealed class RulesEngineTests
{
    private static readonly RuleSet DefaultRules = new(
    [
        new("bottle", Verdict.Ok),
        new("cell phone", Verdict.Ok),
        new("cup", Verdict.Nok),
        new("scissors", Verdict.Danger),
        new("person", Verdict.Ignore),
    ]);

    private static CoreDetection Det(string name) => new(name, 0.9, new NormalizedRect(0.4, 0.4, 0.2, 0.2));

    [Theory]
    [InlineData("bottle", Verdict.Ok)]
    [InlineData("cell phone", Verdict.Ok)]
    [InlineData("cup", Verdict.Nok)]
    [InlineData("scissors", Verdict.Danger)]
    [InlineData("person", Verdict.Ignore)]
    [InlineData("banana", Verdict.Unknown)]
    [InlineData("Scissors", Verdict.Danger)]
    public void Evaluate_DefaultRules_ReturnsExpectedVerdict(string className, Verdict expected)
    {
        Assert.Equal(expected, RulesEngine.Evaluate(Det(className), DefaultRules));
    }

    [Fact]
    public void Evaluate_EmptyRuleSet_ReturnsUnknown()
    {
        Assert.Equal(Verdict.Unknown, RulesEngine.Evaluate(Det("cup"), RuleSet.Empty));
    }

    [Fact]
    public void Evaluate_NewRuleSet_ReflectsChangedRules()
    {
        var detection = Det("cup");
        var reloaded = new RuleSet([new("cup", Verdict.Ok)]);

        Assert.Equal(Verdict.Nok, RulesEngine.Evaluate(detection, DefaultRules));
        Assert.Equal(Verdict.Ok, RulesEngine.Evaluate(detection, reloaded));
    }

    [Fact]
    public void Evaluate_SameInputs_IsDeterministic()
    {
        var detection = Det("scissors");

        Assert.Equal(
            RulesEngine.Evaluate(detection, DefaultRules),
            RulesEngine.Evaluate(detection, DefaultRules));
    }
}
