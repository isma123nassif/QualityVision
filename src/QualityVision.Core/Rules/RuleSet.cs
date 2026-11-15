using System.Collections.Frozen;

namespace QualityVision.Core.Rules;

/// <summary>
/// Immutable, case-insensitive map "class name → <see cref="Verdict"/>". A class that is not listed gives
/// <see cref="Verdict.Unknown"/>. Built once and never mutated, so one instance can be shared across threads (ADR-002).
/// </summary>
public sealed class RuleSet
{
    private readonly FrozenDictionary<string, Verdict> _rules;

    /// <summary>A rule set without rules: every class is <see cref="Verdict.Unknown"/>.</summary>
    public static RuleSet Empty { get; } = new([]);

    /// <summary>Copies <paramref name="rules"/>; later changes to the source have no effect.</summary>
    /// <param name="rules">Class name → verdict pairs.</param>
    /// <exception cref="ArgumentException">A class name is blank, or two names differ only in case.</exception>
    public RuleSet(IEnumerable<KeyValuePair<string, Verdict>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var copy = new Dictionary<string, Verdict>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, Verdict verdict) in rules)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A rule has a blank class name.", nameof(rules));
            }

            if (!Enum.IsDefined(verdict))
            {
                throw new ArgumentException($"Rule '{name}' has an undefined verdict value.", nameof(rules));
            }

            if (!copy.TryAdd(name, verdict))
            {
                throw new ArgumentException($"Duplicate rule for class '{name}' (names are case-insensitive).", nameof(rules));
            }
        }

        _rules = copy.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The configured rules (read-only, case-insensitive keys).</summary>
    public IReadOnlyDictionary<string, Verdict> Rules => _rules;

    /// <summary>Verdict for <paramref name="className"/>; <see cref="Verdict.Unknown"/> if the class is not listed.</summary>
    /// <param name="className">COCO class name, compared case-insensitively.</param>
    public Verdict VerdictFor(string className)
    {
        ArgumentNullException.ThrowIfNull(className);
        return _rules.TryGetValue(className, out Verdict verdict) ? verdict : Verdict.Unknown;
    }
}
