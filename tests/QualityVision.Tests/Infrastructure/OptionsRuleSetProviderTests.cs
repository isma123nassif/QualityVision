using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Rules;

namespace QualityVision.Tests.Infrastructure;

public sealed class OptionsRuleSetProviderTests
{
    private sealed class FakeOptionsMonitor(RulesOptions initial) : IOptionsMonitor<RulesOptions>
    {
        private readonly List<Action<RulesOptions, string?>> _listeners = [];

        public RulesOptions CurrentValue { get; private set; } = initial;

        public int ListenerCount => _listeners.Count;

        public RulesOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<RulesOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Subscription(this, listener);
        }

        public void Fire(RulesOptions newValue)
        {
            CurrentValue = newValue;
            foreach (var listener in _listeners.ToArray())
            {
                listener(newValue, null);
            }
        }

        private sealed class Subscription(FakeOptionsMonitor owner, Action<RulesOptions, string?> listener) : IDisposable
        {
            public void Dispose() => owner._listeners.Remove(listener);
        }
    }

    private static RulesOptions Options(params (string Name, string Verdict)[] rules)
    {
        var options = new RulesOptions();
        foreach (var (name, verdict) in rules)
        {
            options.Classes[name] = verdict;
        }

        return options;
    }

    private static OptionsRuleSetProvider Create(FakeOptionsMonitor monitor) =>
        new(monitor, NullLogger<OptionsRuleSetProvider>.Instance);

    [Fact]
    public void Current_AfterConstruction_ReflectsInitialRules()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok"), ("person", "ignore")));
        using var provider = Create(monitor);

        Assert.Equal(Verdict.Nok, provider.Current.VerdictFor("cup"));
        Assert.Equal(Verdict.Ignore, provider.Current.VerdictFor("person"));
        Assert.Equal(Verdict.Unknown, provider.Current.VerdictFor("banana"));
    }

    [Fact]
    public void Current_ValidReload_SwapsRuleSet()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok")));
        using var provider = Create(monitor);
        var before = provider.Current;

        monitor.Fire(Options(("cup", "Ok"), ("bottle", "DANGER")));

        Assert.NotSame(before, provider.Current);
        Assert.Equal(Verdict.Ok, provider.Current.VerdictFor("cup"));
        Assert.Equal(Verdict.Danger, provider.Current.VerdictFor("bottle"));
        Assert.Equal(Verdict.Nok, before.VerdictFor("cup"));
    }

    [Fact]
    public void Current_ReloadWithNoRules_KeepsPreviousRuleSet()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok"), ("person", "Ignore")));
        using var provider = Create(monitor);
        var before = provider.Current;

        monitor.Fire(new RulesOptions());

        Assert.Same(before, provider.Current);
        Assert.Equal(Verdict.Ignore, provider.Current.VerdictFor("person"));
    }

    [Fact]
    public void Current_IdenticalReload_DoesNotSwapInstance()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok"), ("person", "Ignore")));
        using var provider = Create(monitor);
        var before = provider.Current;

        monitor.Fire(Options(("PERSON", "ignore"), ("cup", "NOK")));

        Assert.Same(before, provider.Current);
    }

    [Fact]
    public void Current_ReloadWithUnknownVerdictString_KeepsPreviousRuleSet()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok")));
        using var provider = Create(monitor);
        var before = provider.Current;

        monitor.Fire(Options(("cup", "Ok"), ("bottle", "Banana")));

        Assert.Same(before, provider.Current);
        Assert.Equal(Verdict.Nok, provider.Current.VerdictFor("cup"));
    }

    [Fact]
    public void Current_ReloadWithBlankClassName_KeepsPreviousRuleSet()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok")));
        using var provider = Create(monitor);
        var before = provider.Current;

        monitor.Fire(Options((" ", "Ok")));

        Assert.Same(before, provider.Current);
    }

    [Fact]
    public void Current_InvalidReloadThenValidReload_RecoversWithNewRules()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok")));
        using var provider = Create(monitor);

        monitor.Fire(Options(("cup", "oops")));
        monitor.Fire(Options(("cup", "Danger")));

        Assert.Equal(Verdict.Danger, provider.Current.VerdictFor("cup"));
    }

    [Fact]
    public void Constructor_InvalidVerdictString_Throws()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Banana")));

        Assert.Throws<OptionsValidationException>(() => Create(monitor));
    }

    [Fact]
    public void Constructor_BlankClassName_Throws()
    {
        var monitor = new FakeOptionsMonitor(Options(("", "Ok")));

        Assert.Throws<OptionsValidationException>(() => Create(monitor));
    }

    [Fact]
    public void Constructor_EmptyRules_GivesEmptyRuleSet()
    {
        var monitor = new FakeOptionsMonitor(new RulesOptions());
        using var provider = Create(monitor);

        Assert.Empty(provider.Current.Rules);
    }

    [Fact]
    public void Constructor_SubscribesToOnChange()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok")));
        using var provider = Create(monitor);

        Assert.Equal(1, monitor.ListenerCount);
    }

    [Fact]
    public void Dispose_UnsubscribesAndIgnoresLaterChanges()
    {
        var monitor = new FakeOptionsMonitor(Options(("cup", "Nok")));
        var provider = Create(monitor);

        provider.Dispose();
        monitor.Fire(Options(("cup", "Ok")));

        Assert.Equal(0, monitor.ListenerCount);
        Assert.Equal(Verdict.Nok, provider.Current.VerdictFor("cup"));
    }
}
