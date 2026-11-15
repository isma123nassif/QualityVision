## Code review — M4 (RulesEngine, Prüfzone, debounce, hot reload)
Gates: build pass (orchestrator: -warnaserror 0/0) | tests 311/311 (orchestrator) | format clean (orchestrator)
> The reviewer could not re-run the gates: `dotnet build/test/format` needed interactive approval in this session. The figures above are the orchestrator's verified results. Everything else was checked statically against `git diff HEAD` and the new files.

| # | Severity | File:line | Finding | Suggested fix |
|---|----------|-----------|---------|---------------|
| 1 | MAJOR | src/QualityVision.Infrastructure/Rules/OptionsRuleSetProvider.cs:54-62 (with src/QualityVision.Web/Program.cs:18) | **A reload with zero rules is accepted as valid**, so it silently clears every rule, including `person → Ignore`. Two realistic ways this happens: (a) On reload, `FileConfigurationProvider` treats the file as optional. If `rules.json` is briefly missing (editor delete+recreate, rename), the provider sets `Data = {}` and fires `OnReload`. The provider then logs "Rules reloaded: 0 classes", every class becomes `Unknown`, and a person in the zone is **counted as Unbekannt**. (b) A **JSON syntax error** (missing comma, or a duplicate key differing only in case, which the JSON parser rejects) also empties the provider's `Data`, but it throws before `OnReload`. The old rules stay active (good), but our code logs **no warning**: the exception ends up in an unobserved watcher task. The empty `Data` is also latent. The next reload of *any* other source (`appsettings*.json` has `reloadOnChange`) fires `OnChange` with an empty `Classes`, which is accepted and wipes the rules. Hot reload is a highlight of the demo, and ADR-002's goal is that "a typo must not stop the demo". | In `OnRulesChanged`, treat `options.Classes.Count == 0` as invalid **when the current set is non-empty**. Keep the previous set and warn: `"rules.json is empty, missing or unreadable – keeping the previous {Count} rules"`. The startup case (`Constructor_EmptyRules_GivesEmptyRuleSet`) can stay as it is. Add a test: valid → `Fire(new RulesOptions())` → `Same(before, Current)`. Optionally set `OnLoadException = ctx => { log; ctx.Ignore = true; }` on the `rules.json` source so syntax errors are logged. |
| 2 | MINOR | src/QualityVision.Infrastructure/Rules/OptionsRuleSetProvider.cs:61-62 | **`OnChange` fires more than once.** File watchers often fire twice per save, and the `IConfigurationRoot` reload token also fires when `appsettings*.json` changes. Each time, the provider rebuilds and logs "Rules reloaded: N classes" even if nothing changed. This is functionally harmless: the rebuild is idempotent, the swap is atomic, and the tracker does not depend on the rules instance. But the log is noisy and confusing when explaining the feature live. | Before swapping, compare `next.Rules` with `Current.Rules` (same count, and every key has an equal verdict). If they match, return without logging, or log at Debug level. |
| 3 | MINOR | src/QualityVision.Web/Pipeline/DetectionService.cs:148-151 | **Changing `AbsenceFrames` on reload recreates the tracker without state.** Every class currently in the zone counts again on the next frame. The contract requires the recreation (step 8), but this side effect is not documented. | Prime the new tracker by calling `_tracker.Update(inZone)` once and discarding the result. This may miss a genuinely new entry in that single frame. Alternatively, document the recount in ADR-002 or the learning notes. |
| 4 | MINOR | src/QualityVision.Web/Pipeline/InspectionLog.cs:83 + DetectionService.cs:164 | **A throwing `EventAdded` subscriber is not isolated** (relevant once M5 adds SignalR). The exception propagates into `CountZoneEntries`. The remaining entries of that frame are lost: the tracker has already marked them Present, so they are never counted. The frame is also not published to the stream (the per-frame catch at :126 swallows it). The handler being invoked outside the lock is correct. | In `Add`, iterate `GetInvocationList()` and wrap each handler in try/catch with a logged warning. Or make it a contract that handlers never throw, and enforce it in the M5 hub bridge. |
| 5 | MINOR | src/QualityVision.Web/Pipeline/InspectionLog.cs:35 (contract-defined) | **Ausschussquote = NOK / (OK+NOK+DANGER+UNKNOWN).** I argue that Danger should **not** count as scrap. Danger is a *safety* event (a foreign object or hazard in the cell, e.g. scissors), not a quality verdict on a part. Unknown is "not a part we inspect" at all. Including both in the denominator dilutes the rate: a couch or a phone passing through lowers the "Ausschussquote". In German quality practice, *Ausschussquote = n.i.O.-Teile / geprüfte Teile*. An interviewer from QA is likely to challenge the current definition. | Use `ScrapRate = Ok + Nok == 0 ? 0 : (double)Nok / (Ok + Nok)`, and keep Gefahr and Unbekannt as separate KPIs. Decide this (architect/orchestrator) before M5 builds the dashboard on it. Then update `InspectionLogTests.cs:209-228` and the contract. |
| 6 | MINOR | src/QualityVision.Web/Program.cs:18 | `rules.json` is read from `ContentRootPath`. Under `dotnet run` that is the project folder, which was verified live and is correct. When the app is **published**, the content root defaults to the *current working directory*, not the app folder. If it is started from elsewhere, `optional: false` makes startup fail with FileNotFoundException. That fails fast, which is acceptable, but it can surprise. The `Path.Combine` is also redundant, because the builder's base path is already the content root (harmless, explicit). | In `run-demo.sh` (M6/M7), `cd` into the app or publish directory, or pass `--contentRoot`. Note this next to the csproj comment at `QualityVision.Web.csproj:17-19`. |
| 7 | MINOR | src/QualityVision.Web/Endpoints/PipelineEndpoints.cs:20,28 | `inZone` is computed against the *current* zone, not the zone used when the frame was processed. It is also reported as `true` for `Ignore` detections, while `FilterInside` and the tracker exclude them. This can confuse anyone checking the API against the counts. | Use `inZone = c.Verdict != Verdict.Ignore && InspectionZone.IsInside(...)`, or document the field as purely geometric. |
| 8 | MINOR | src/QualityVision.Web/Endpoints/InspectionEndpoints.cs:19-20 | `Counts` and `Recent` are read under two separate locks, so the summary can be inconsistent by one event (counts include an event that `recent` does not show yet). This is negligible at the demo's rates. | Optionally add `IInspectionLog.Snapshot(int max)` returning `(counts, recent)` taken under one lock, which M5 can reuse. |
| 9 | MINOR | tests/QualityVision.Tests/Web/DetectionServiceTests.cs:55-77 | **The wiring is not unit-tested.** The only DetectionService test predates M4; it just gained the new constructor arguments. Not covered: (a) a detection **exception** mid-presence does not count as an absence frame (contract step 9, a real debounce edge case); (b) an object outside the zone is not logged; (c) an entry reaches `IInspectionLog` with sequence 1, 2, …. All of this was verified only live. | Add 2-3 tests with a scripted fake detector (present, throw, present → `Counts.Total == 1`) and a zone that excludes one box. |
| 10 | MINOR | tests/QualityVision.Tests/Infrastructure/OptionsRuleSetProviderTests.cs:124,132 | `Assert.ThrowsAny<Exception>` is too weak: any exception, such as a NullReferenceException, would pass. | `Assert.Throws<OptionsValidationException>(...)`. |
| 11 | MINOR | tests/QualityVision.Tests/Infrastructure/SkiaFrameRendererTests.cs | The renderer tests cover Ok green, Nok red, Ignore not drawn, empty zone white and Nok zone red. Not covered: the **Danger** zone (orange, label "Prüfzone · GEFAHR", 6 px stroke) and the **dimming** of objects outside the zone. | Add one test that samples a Danger zone edge (orange) and one that compares edge alpha/brightness inside vs outside the zone. |
| 12 | MINOR | src/QualityVision.Core/Inspection/ZoneEntryTracker.cs:43-44,87 | Each frame allocates a `Dictionary`, two `List`s and the result, even when `inZone` and `_present` are both empty (the common case). This is negligible at about 10 FPS. Memory growth is **not** a problem: `_present` only holds currently present classes and removes them after N misses, so it is bounded by the number of classes in view (≤ 80 COCO). `_presentKeys` is reused. | Optional: return `[]` early when both are empty, and reuse `best`/`order` as fields (the tracker is single-threaded). |
| 13 | MINOR | src/QualityVision.Web/Components/Pages/Home.razor:118 | Ignored detections, such as "Person", appear in the detection list with a grey "Ignoriert" badge styled as `unknown`. They look identical to Unbekannt. The tile "Gefahr" (:29) and the badge "GEFAHR" differ in casing. This is acceptable but inconsistent. | Give `Ignore` its own muted class, or hide Ignore entries from the list. Pick one casing for Gefahr/GEFAHR. |
| 14 | MINOR | src/QualityVision.Web/Pipeline/DetectionService.cs:158 | `Sequence` is monotonic per run, starting at 1. `Interlocked` is not needed: the field is only touched by the single loop. It is harmless, but it suggests concurrency that does not exist. Sequence numbers restart after a restart. | For M6, do **not** use `Sequence` as the SQLite key; use an autoincrement id. Optionally use a plain `++_eventSequence`. |

### Focus questions — answers
- **ZoneEntryTracker vs ADR-002:** The tracker is correct for every sequence I traced:
  - first appearance and continuous presence count once;
  - a gap of N−1 never recounts (the miss counter resets on sight; `ZoneEntryTracker.cs:70-73,95`);
  - a gap of N frames removes the class (`:76-79`), and a reappearance then counts;
  - N = 1 recounts after a single miss;
  - several detections of one class give one entry with the highest confidence, and a tie keeps the first (`:58`);
  - `Ignore` is skipped (`:47`); a class seen only as `Ignore` while Present counts as absence, which is correct;
  - class names are case-insensitive;
  - `Reset` clears all state;
  - a rules change during presence does not recount.

  The per-class (not per-object) limitation is explicit and pinned by tests (`ZoneEntryTrackerTests.cs:258-280`).
- **Single-threaded assumption:** It holds. `_tracker` is only touched inside the sequential `await foreach` loop (`DetectionService.cs:77-138`). The awaits provide the memory barriers between continuations.
- **RuleSet immutability:** `RuleSet` copies its input into a `FrozenDictionary` (`RuleSet.cs:23-42`) and exposes it read-only. The test checks that mutating the source has no effect. Safe to share across threads.
- **Atomic swap:** `Volatile.Write`/`Volatile.Read` (`OptionsRuleSetProvider.cs:44,61`) are correct. `DetectionService` reads `Current` once per frame (`:98`). Firing `OnChange` twice is harmless (#2), but an *empty* reload is not (#1).
- **InspectionLog:** All state is under a single `Lock`. `EventAdded` is invoked outside it (`:83`), and the re-entrancy test is good (`InspectionLogTests.cs:304`). Handler exceptions are not isolated (#4).
- **Renderer:**
  - colours match the contract;
  - Ignore is not drawn;
  - objects outside the zone use alpha 153 (≈ 60 %) and a stroke of about 0.6×, and their label background is dimmed too (`:184`);
  - the zone shows the worst verdict by `VerdictSeverity.Rank` and is white when empty;
  - Nok/Danger use a 1.5× stroke (4 → 6 px at the reference width) and the label "Prüfzone · n.i.O./GEFAHR";
  - the zone is drawn last, so it stays on top.
- **German UI:**
  - "i.O.", "n.i.O.", "GEFAHR", "Unbekannt", "Ignoriert", "Letzte Prüfungen", "Noch keine Prüfungen" and "Ausschussquote" are correct;
  - the percentages use de-DE formatting ("12,5 %");
  - the time is shown as HH:mm:ss in server-local time (the server is the demo PC).
- **Do the tests really cover the debounce edge cases?** For the tracker, yes, and they go beyond the contract:
  - all 10 contract items;
  - the N−1/N boundary as a theory over N ∈ {1, 3, 5, 10};
  - the miss-counter reset between gaps;
  - case-insensitivity;
  - Ignore alongside another class;
  - Reset with pending misses;
  - the per-class limitation.

  The gap is at the integration level (#9): "a failed detection frame is not an absence frame".

### Checklist notes
- Architecture: Core uses only the BCL (`System.Collections.Frozen`). `IOptionsMonitor` is used only in `OptionsRuleSetProvider` and in Web. There is no `IRulesEngine` interface (as agreed). There is no speculative abstraction.
- Conventions:
  - `TimeProvider` is used for timestamps;
  - the log template is structured (`DetectionService.cs:165`);
  - there is no `Version=` in any csproj;
  - there is no `DateTime.Now`, `.Result`/`.Wait()` (except the documented completed-task case in M2), `async void` or `MarkupString`.
- `AbsenceFrames` is validated with `[Range(1,100)]`, `ValidateDataAnnotations` and `ValidateOnStart` (`Program.cs:34-37`). An invalid hot reload of `InspectionOptions` is caught per frame (`DetectionService.cs:126-131`).

Positives:
- Clean ADR-002 implementation: a pure `RulesEngine`, an immutable `RuleSet`, and one snapshot per frame. `TryParseVerdict` also rejects numeric and flag-combination values that `Enum.TryParse` would silently accept (`OptionsRuleSetProvider.cs:120-124`).
- `ZoneEntryTracker` is small, correct and exhaustively tested, and a failed detection deliberately does not feed it (`DetectionService.cs:113-116`).
- `InspectionLog` is a tidy ring buffer with events raised outside the lock, and it has a concurrency test.

Verdict: **APPROVE**. There is no BLOCKER and the gates are green per the orchestrator. MAJOR #1 should be fixed in this milestone: it is a 3-line guard plus one test. Decide #5 (Ausschussquote definition) before M5.
