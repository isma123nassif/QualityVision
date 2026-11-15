# ADR-002: Rules engine boundary and debounce unit

- **Status:** Accepted
- **Date:** 2026-11-15
- **Deciders:** architect, orchestrator, project owner
- **Refines:** [ADR-001](ADR-001-architecture.md) §3 and §5 (M1 review findings #4 and #10)

## Context

ADR-001 puts `RulesEngine` and `ZoneEntryTracker` in Core and requires rules to hot-reload from
`rules.json` via `IOptionsMonitor`. These two statements collide: `IOptionsMonitor<T>` lives in
`Microsoft.Extensions.Options`, a package Core must not reference (zero-package rule, enforced by
`ArchitectureTests`). The M1 review (#4) also found that the QA brief asked for a Core test of
`IOptionsMonitor` changes, which cannot compile against Core.

ADR-001 also described the debounce both as time-based (§4) and as frame-based (§5) (review #10).
These behave differently at 10 FPS (live camera) and 30 FPS (video file).

Both contracts must be fixed before M4, where backend-dev (implementation) and qa-engineer (tests)
work in parallel against the same signatures.

## Decision

### 1. `RulesEngine` is pure; rules arrive as an immutable `RuleSet`

- Core defines `RuleSet`: an immutable map *class name → `Verdict`*, **case-insensitive**
  (`StringComparer.OrdinalIgnoreCase`), with a fixed fallback of `Verdict.Unknown` for any class
  not listed. It is constructed once and never mutated, so it can be shared across threads freely.
- `RulesEngine.Evaluate(Detection, RuleSet)` is a pure function: same inputs, same verdict. It has no
  state, no configuration dependency and no clock.
- Core defines the port `IRuleSetProvider { RuleSet Current { get; } }`. `DetectionService` reads
  `Current` **once per frame**, so all detections of a frame are judged against the same snapshot.
- Infrastructure implements the port as `OptionsRuleSetProvider`, the only class that touches
  `IOptionsMonitor<RulesOptions>`. It builds a `RuleSet` at construction (invalid rules at startup →
  fail fast), caches it, and on `OnChange` rebuilds and swaps it atomically (`Volatile.Write`). If a
  reloaded `rules.json` contains an unknown verdict or empty class name, it **keeps the last valid
  `RuleSet`** and logs a warning — a typo in the file must not stop the demo. It disposes its
  `OnChange` subscription.
- `RulesOptions` binds verdicts as **strings**, and the adapter parses them with
  `Enum.TryParse(ignoreCase: true)`. That way a bad value is handled by our mapping code instead of
  failing inside the configuration binder on a file-watcher thread.
- It lives in Infrastructure (not Web) so the test project, which references Core and
  Infrastructure only, can test it.

The interface has two concrete uses: `OptionsRuleSetProvider` in production and a trivial fixed
provider in tests. There is **no** `IRulesEngine` interface: a pure function needs no fake.

### 2. Debounce unit: consecutive evaluated frames of absence

- `ZoneEntryTracker` counts **evaluated frames** (frames that went through detection), not time.
- A class is *present* in a frame if at least one of its detections has its box centre inside the
  zone and its verdict is not `Ignore`. Initial state for every class: *Absent*.
- *Absent → Present* emits exactly one entry. While *Present*, each frame without the class
  increments a miss counter (reset to 0 when the class is seen). When the counter reaches **N** the
  class becomes *Absent*. A gap of N − 1 frames therefore never recounts; a gap of N frames followed by
  a reappearance counts again.
- N is configured as `Inspection:AbsenceFrames` (integer, default **5**, valid range 1–100,
  validated at startup). The tracker is single-threaded by contract (only `DetectionService` calls it).
- `TimeProvider` is **not** a dependency of the tracker. Event timestamps are taken by the caller
  (`DetectionService`) from the injected `TimeProvider`.

**FPS dependency, accepted.** N = 5 is ~0.5 s at 10 FPS and ~0.17 s at 30 FPS. This is acceptable
because detector flicker is a per-frame effect, so frames are its natural unit, and in the demo
objects are removed for well over a second, so both windows separate "flicker" from "removed".
Frames also keep the tracker a pure function of its input sequence. If the measured detection rate
on demo day differs a lot, N is retuned in configuration without a code change.

### 3. Signature sketch (Core, no packages)

```csharp
namespace QualityVision.Core.Rules;

public enum Verdict { Ok, Nok, Danger, Unknown, Ignore }

/// <summary>Immutable, case-insensitive class → verdict map. Missing class → Unknown.</summary>
public sealed class RuleSet
{
    public static RuleSet Empty { get; }
    public RuleSet(IEnumerable<KeyValuePair<string, Verdict>> rules); // copies; throws on blank/duplicate (case-insensitive) names
    public IReadOnlyDictionary<string, Verdict> Rules { get; }
    public Verdict VerdictFor(string className);
}

public interface IRuleSetProvider
{
    RuleSet Current { get; }
}

public static class RulesEngine
{
    public static Verdict Evaluate(Detection detection, RuleSet rules);
}

public sealed record ClassifiedDetection(Detection Detection, Verdict Verdict);
```

```csharp
namespace QualityVision.Core.Inspection;

public sealed class ZoneEntryTracker
{
    public ZoneEntryTracker(int absenceFrames = 5);                 // ArgumentOutOfRangeException if < 1
    /// <summary>Call exactly once per evaluated frame with the detections inside the zone.
    /// Returns the detections that count as a new entry in this frame (at most one per class).</summary>
    public IReadOnlyList<ClassifiedDetection> Update(IReadOnlyList<ClassifiedDetection> inZone);
}
```

Infrastructure (adapter, may use `Microsoft.Extensions.Options` / `Logging`):

```csharp
namespace QualityVision.Infrastructure.Rules;

public sealed class RulesOptions
{
    public const string SectionName = "Rules";
    public Dictionary<string, string> Classes { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OptionsRuleSetProvider(IOptionsMonitor<RulesOptions> monitor,
                                           ILogger<OptionsRuleSetProvider> logger)
    : IRuleSetProvider, IDisposable;
```

`rules.json`:

```json
{ "Rules": { "Classes": { "bottle": "Ok", "cup": "Nok", "scissors": "Danger", "person": "Ignore" } } }
```

### 4. Testing consequences

- `RulesEngine` / `RuleSet`: plain unit tests; hot reload is expressed as "evaluate with a new
  `RuleSet`" — no configuration system involved.
- `OptionsRuleSetProvider`: tested in isolation with a hand-written fake `IOptionsMonitor<RulesOptions>`
  that fires its `OnChange` listeners: new rules visible via `Current`, invalid reload keeps the
  previous `RuleSet`, `Dispose` unsubscribes.
- `ZoneEntryTracker`: feed frame sequences; no `FakeTimeProvider` needed. `FakeTimeProvider` is used
  only where wall-clock time matters (timestamps, per-minute statistics, health, backoff).

## Consequences

**Positive**

- Core keeps zero packages; `ArchitectureTests` stays green without exceptions.
- Rules and debounce logic are deterministic and testable from a sequence of inputs.
- A broken `rules.json` during the demo degrades to "old rules still active" instead of an exception.
- Easy to explain in an interview: "the domain gets a snapshot; the adapter knows where it comes from."

**Negative / accepted trade-offs**

- One extra port and adapter (`IRuleSetProvider`, `OptionsRuleSetProvider`) compared with injecting
  `IOptionsMonitor` directly.
- The debounce window in seconds depends on the detection rate; documented and tunable via `N`.
- Infrastructure needs `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions`
  (via `FrameworkReference Microsoft.AspNetCore.App` or the corresponding packages).

## Alternatives considered

| Alternative | Why rejected |
|---|---|
| Inject `IOptionsMonitor<RulesOptions>` into `RulesEngine` in Core | Breaks the zero-package rule for Core. |
| `RulesEngine` constructor taking `Func<RuleSet>` | Works, but a delegate is less self-documenting than a named port and harder to register and explain; the per-frame snapshot is clearer at the call site. |
| Move `RulesEngine` to Infrastructure | Puts domain logic next to adapters; Core would lose its most important business rule. |
| Copy a minimal `IOptionsMonitor`-like interface into Core | Re-implements a framework type for no gain; `IRuleSetProvider` is smaller and domain-named. |
| Time-based debounce (`TimeProvider`, minimum absence duration) | Independent of FPS, but adds a clock dependency to the tracker and more test setup; frame flicker is what we actually suppress. Can be added later as an optional minimum duration if needed. |
| Hybrid (N frames **and** minimum duration) | Two parameters to tune and explain for no observable benefit at demo frame rates. |
