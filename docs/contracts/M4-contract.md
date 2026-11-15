# M4 contract — RulesEngine, Prüfzone, debounce, hot reload

Follows **ADR-002**, whose signatures are binding. Read it first. Code and comments are in English, UI text in German.

## Core (`src/QualityVision.Core`, zero packages) — owner: backend-dev

### Rules
`Rules/Verdict.cs`, `Rules/RuleSet.cs`, `Rules/IRuleSetProvider.cs`, `Rules/RulesEngine.cs` and `Rules/ClassifiedDetection.cs` are implemented exactly as in ADR-002 §3:
- `enum Verdict { Ok, Nok, Danger, Unknown, Ignore }`
- `RuleSet` is immutable and case-insensitive. A missing class gives `Unknown`. A blank or duplicate name throws `ArgumentException`. It has `Empty`.
- `RulesEngine.Evaluate(Detection, RuleSet)` is pure.
- `record ClassifiedDetection(Detection Detection, Verdict Verdict)`.

### `Rules/VerdictSeverity.cs`
`public static int Rank(Verdict v)`: Danger 4 > Nok 3 > Unknown 2 > Ok 1 > Ignore 0. It is used for the zone colour and in tests.

### Inspection
**`Inspection/ZoneEntryTracker.cs`**, exactly as in ADR-002 §2/§3:
- `ZoneEntryTracker(int absenceFrames = 5)`, `IReadOnlyList<ClassifiedDetection> Update(IReadOnlyList<ClassifiedDetection> inZone)`.
- The tracker is per class. *Absent → Present* emits one entry, and when one class has several detections in the frame, the entry is the one with the highest confidence. N consecutive misses make the class Absent again.
- Detections with `Ignore` are ignored even if passed in.
- `public void Reset()` clears all state.

**`Inspection/InspectionZone.cs`**, a static helper:
- `static bool IsInside(Detection d, NormalizedRect zone)` checks `zone.Contains(d.Center.X, d.Center.Y)`.
- `static IReadOnlyList<ClassifiedDetection> FilterInside(IEnumerable<ClassifiedDetection>, NormalizedRect zone)` excludes `Ignore`.

**`Inspection/InspectionEvent.cs`**:
```csharp
public sealed record InspectionEvent(
    long Sequence,                 // monotonic per app run, assigned by the caller
    DateTimeOffset Timestamp,      // from TimeProvider, taken by the caller
    string ClassName,              // COCO name, e.g. "cup"
    string DisplayName,            // German, GermanLabels.Get
    Verdict Verdict,
    double Confidence);
```

**`Inspection/VerdictLabels.cs`**: German short labels.

| Verdict | Label |
|---|---|
| Ok | `"i.O."` |
| Nok | `"n.i.O."` |
| Danger | `"GEFAHR"` |
| Unknown | `"Unbekannt"` |
| Ignore | `"Ignoriert"` |

`i.O.`/`n.i.O.` = *in Ordnung* / *nicht in Ordnung*, the standard terms in German quality inspection.

## Infrastructure — owner: backend-dev
**`Rules/RulesOptions.cs`** (section `"Rules"`): `Dictionary<string, string> Classes { get; set; } = new(StringComparer.OrdinalIgnoreCase)`, mapping a COCO class name to a verdict string.

**`Rules/OptionsRuleSetProvider.cs`**: `IRuleSetProvider, IDisposable`, singleton, exactly as in ADR-002 §1.
- It builds the rule set at construction and fails fast on invalid rules.
- On `OnChange` it rebuilds and swaps atomically with `Volatile.Write`.
- An invalid reload keeps the last valid `RuleSet` and logs a warning; a successful one logs `"Rules reloaded: {Count} classes"`.
- It disposes its subscription.
- Verdicts are parsed with `Enum.TryParse(ignoreCase: true)`.

DI: register it as the `IRuleSetProvider` singleton.

## Web — owner: backend-dev

### `src/QualityVision.Web/rules.json`
A new file, copied to the output with `CopyToOutputDirectory=PreserveNewest`. Load it in `Program.cs` with `AddJsonFile("rules.json", optional: false, reloadOnChange: true)`, **after** `appsettings*.json`.
```json
{ "Rules": { "Classes": {
  "bottle": "Ok", "cell phone": "Ok", "cup": "Nok", "scissors": "Danger", "person": "Ignore"
} } }
```
**Important for hot reload:**
- The file watcher must watch the file the app actually reads.
- The app runs from `bin/…` with content root `src/QualityVision.Web`. Read `rules.json` from the **content root** (not the bin copy) so editing `src/QualityVision.Web/rules.json` reloads live.
- The `<Content>`/`<None>` item may still copy it to the output for publish scenarios.

### `InspectionOptions`
Add `int AbsenceFrames { get; set; } = 5`, validated in the range 1–100 with `ValidateOnStart`, plus `appsettings.json`.

### `Pipeline/InspectionLog.cs`
A singleton, thread-safe class implementing:
```csharp
public interface IInspectionLog
{
    void Add(InspectionEvent e);
    IReadOnlyList<InspectionEvent> Recent(int max = 50);          // newest first, ring buffer of 500
    InspectionCounts Counts { get; }                               // since app start (M6 will load from DB)
    event Action<InspectionEvent>? EventAdded;                     // for M5 SignalR; invoked outside locks
}
public readonly record struct InspectionCounts(int Ok, int Nok, int Danger, int Unknown)
{
    public int Total => Ok + Nok + Danger + Unknown;
    public double ScrapRate => Ok + Nok == 0 ? 0 : (double)Nok / (Ok + Nok);   // Ausschussquote: NOK / (OK + NOK); Danger/Unknown are separate KPIs
}
```

### `DetectionService`, per frame
1. Run detections as in M3.
2. `var rules = ruleSetProvider.Current;` once per frame.
3. Classify every detection with `RulesEngine.Evaluate`.
4. `inZone = InspectionZone.FilterInside(classified, zone)`.
5. `entries = tracker.Update(inZone)`.
6. For each entry, build an `InspectionEvent` (sequence via `Interlocked.Increment`, `TimeProvider.GetUtcNow()`), then `inspectionLog.Add(e)` and log it with `"Inspection {Sequence}: {Class} → {Verdict} ({Confidence:P0})"`.
7. Render with the classified detections (see Vision).
8. The tracker is owned by `DetectionService`. It is single-threaded and created once with `AbsenceFrames`; if `AbsenceFrames` changes on reload, recreate it.
9. If detection failed this frame (the M3 fallback), skip steps 3–6 but still render the zone.

### `PipelineMetrics`
`LastDetections` becomes `IReadOnlyList<ClassifiedDetection>`. Update `/api/pipeline/metrics` to add `verdict` and `inZone` per detection.

### `GET /api/inspection/summary`
Returns `{ counts: {ok, nok, danger, unknown, total, scrapRate}, recent: [ last 20 events ] }`.

## Vision — owner: vision-engineer (renderer only)

### `SkiaFrameRenderer.RenderJpeg`
It now takes `IReadOnlyList<ClassifiedDetection>` instead of `Detection`. Keep the overload set minimal; update the callers in Web together with backend-dev's `DetectionService`. The signature is:
```csharp
byte[] RenderJpeg(Frame frame, IReadOnlyList<ClassifiedDetection> detections, NormalizedRect? zone, int quality, int maxWidth)
```

### Colours by verdict

| Verdict | Colour | Hex |
|---|---|---|
| Ok | green | `#22c55e` |
| Nok | red | `#ef4444` |
| Danger | orange | `#f97316` |
| Unknown | grey | `#9ca3af` |
| Ignore | not drawn at all | — |

### Label
`"{GermanLabels.Get} {conf:P0} · {VerdictLabels.Get}"`, e.g. "Tasse 82 % · n.i.O.". Objects **outside** the zone are drawn at reduced opacity (about 60 %, thinner stroke), because only objects in the zone are judged.

### Prüfzone
- **Colour:** the most severe verdict (by `VerdictSeverity.Rank`) among the non-ignored detections whose centre is inside the zone. White `#e5e7eb` when the zone is empty.
- **Label:** "Prüfzone", or "Prüfzone · n.i.O." / "Prüfzone · GEFAHR" when such an object is inside.
- **Danger/Nok:** the zone stroke is 6 px instead of 4 px.

## Frontend — owner: frontend-dev
Minimal for M4; the full dashboard comes in M5. In `Home.razor`, polling `IInspectionLog` every 1 s:
- **Counter tiles:** "i.O." (green), "n.i.O." (red), "Gefahr" (orange), "Unbekannt" (grey), and "Ausschussquote" as a percentage with one decimal.
- **"Letzte Prüfungen":** a list of the last 10 events showing time HH:mm:ss, German name, a coloured verdict badge and confidence.
- The detection list from M3 gains the verdict badge per item.

## Tests — owner: qa-engineer (written IN PARALLEL against this contract and ADR-002)
- **`RuleSet`:**
  - case-insensitivity;
  - an unknown class gives `Unknown`;
  - blank and duplicate keys throw;
  - immutability: mutating the source dictionary after construction has no effect.
- **`RulesEngine`:** each default rule (bottle, cell phone, cup, scissors, person) and an unlisted class such as `"banana"` giving `Unknown`.
- **`ZoneEntryTracker`** (the most important part):
  1. first appearance counts once;
  2. continuous presence over 50 frames still counts once;
  3. a one-frame flicker gap does not recount;
  4. a gap of N−1 does not recount, a gap of N then reappearance does;
  5. two classes are independent;
  6. two detections of the same class in one frame give one entry, the highest confidence;
  7. `Ignore` never counts;
  8. N = 1 edge case;
  9. N < 1 throws;
  10. `Reset`.
- **`InspectionZone`:** the centre exactly on an edge counts as inside; a box that overlaps but whose centre is outside does not count.
- **`VerdictSeverity`** ordering and **`VerdictLabels`**.
- **`OptionsRuleSetProvider`:** use a test `IOptionsMonitor<RulesOptions>` whose `OnChange` you can trigger.
  - a valid reload swaps the rules;
  - an invalid reload (bad verdict string) keeps the previous `RuleSet`;
  - an invalid initial configuration throws;
  - `Dispose` unsubscribes.
- **`InspectionLog`:** the counts and `ScrapRate`, the ring buffer cap and newest-first order, and that `EventAdded` is raised.
