using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using QualityVision.Core.Rules;

namespace QualityVision.Infrastructure.Rules;

/// <summary>
/// Adapter from <see cref="IOptionsMonitor{TOptions}"/> to the Core port <see cref="IRuleSetProvider"/> (ADR-002) and the
/// only class that knows about <see cref="RulesOptions"/>. Builds an immutable <see cref="RuleSet"/> at construction
/// (invalid rules → fail fast) and swaps it atomically when <c>rules.json</c> changes. An invalid reload keeps the
/// last valid rule set and logs a warning, so a typo in the file cannot stop the demo.
/// </summary>
public sealed class OptionsRuleSetProvider : IRuleSetProvider, IDisposable
{
    private readonly ILogger<OptionsRuleSetProvider> _logger;
    private readonly IDisposable? _subscription;
    private RuleSet _current;

    /// <summary>Builds the initial rule set and subscribes to changes.</summary>
    /// <param name="monitor">Source of the (hot-reloadable) rules.</param>
    /// <param name="logger">Logger for reloads and rejected configurations.</param>
    /// <exception cref="OptionsValidationException">The initial rules are invalid.</exception>
    public OptionsRuleSetProvider(IOptionsMonitor<RulesOptions> monitor, ILogger<OptionsRuleSetProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        if (!TryBuild(monitor.CurrentValue, out RuleSet? initial, out string? error))
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(RulesOptions), [error]);
        }

        _current = initial;
        _logger.LogInformation("Rules loaded: {Count} classes", initial.Rules.Count);
        _subscription = monitor.OnChange(OnRulesChanged);
    }

    /// <inheritdoc />
    public RuleSet Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public void Dispose() => _subscription?.Dispose();

    private void OnRulesChanged(RulesOptions options, string? name)
    {
        // Runs on the file-watcher thread: it must never throw.
        try
        {
            if (!TryBuild(options, out RuleSet? next, out string? error))
            {
                _logger.LogWarning("Invalid rules.json ignored, keeping the previous {Count} rules: {Error}",
                    Current.Rules.Count, error);
                return;
            }

            RuleSet previous = Current;
            if (next.Rules.Count == 0 && previous.Rules.Count > 0)
            {
                // A missing, unreadable or emptied rules.json looks like "no rules": that must not wipe the rules.
                _logger.LogWarning("rules.json is empty, missing or unreadable – keeping the previous {Count} rules",
                    previous.Rules.Count);
                return;
            }

            if (SameRules(previous, next))
            {
                // File watchers often fire twice per save, and unrelated config reloads also raise OnChange.
                _logger.LogDebug("Rules unchanged ({Count} classes), nothing to swap", next.Rules.Count);
                return;
            }

            Volatile.Write(ref _current, next);
            _logger.LogInformation("Rules reloaded: {Count} classes", next.Rules.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reloading rules failed, keeping the previous {Count} rules", Current.Rules.Count);
        }
    }

    private static bool SameRules(RuleSet a, RuleSet b)
    {
        if (a.Rules.Count != b.Rules.Count)
        {
            return false;
        }

        foreach ((string name, Verdict verdict) in a.Rules)
        {
            if (!b.Rules.TryGetValue(name, out Verdict other) || other != verdict)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBuild(RulesOptions? options, [NotNullWhen(true)] out RuleSet? ruleSet,
        [NotNullWhen(false)] out string? error)
    {
        ruleSet = null;
        IEnumerable<KeyValuePair<string, string>> classes =
            options?.Classes ?? Enumerable.Empty<KeyValuePair<string, string>>();
        var rules = new List<KeyValuePair<string, Verdict>>();
        foreach ((string className, string? value) in classes)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                error = "A rule has an empty class name.";
                return false;
            }

            if (!TryParseVerdict(value, out Verdict verdict))
            {
                error = string.Format(CultureInfo.InvariantCulture,
                    "Rule '{0}' has the unknown verdict '{1}' (allowed: {2}).",
                    className, value, string.Join(", ", Enum.GetNames<Verdict>()));
                return false;
            }

            rules.Add(new(className, verdict));
        }

        try
        {
            ruleSet = new RuleSet(rules);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryParseVerdict(string? value, out Verdict verdict)
    {
        verdict = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();

        // Enum.TryParse also accepts numbers ("3") and flag lists ("Ok,Nok"); only a single verdict name is valid.
        return !trimmed.Contains(',', StringComparison.Ordinal)
            && !char.IsDigit(trimmed[0]) && trimmed[0] is not ('-' or '+')
            && Enum.TryParse(trimmed, ignoreCase: true, out verdict)
            && Enum.IsDefined(verdict);
    }
}
