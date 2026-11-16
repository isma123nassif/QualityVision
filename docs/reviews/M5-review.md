# M5 review — Real-time dashboard, alarm and rules editor

**Scope:** uncommitted changes (`git diff HEAD` + untracked) against `docs/contracts/M5-contract.md` and `CLAUDE.md`.
**Focus:** `EventAdded` thread-safety and disposal in Blazor, `RulesFileService` (atomic write, validation, path),
JS interop error handling, `PerMinute` correctness.
**Gates (verified by orchestrator):** build -warnaserror ✔, 364 tests ✔, format ✔, Chromium E2E ✔ (live dashboard;
save in the rules editor → hot reload logged).

## Verdict: **no BLOCKER — 1 MAJOR (small fix, recommended before the demo), MINORs below**

---

## What is correct (checked in detail)

**`Home.razor`: `EventAdded` handling**
- `OnEventAdded` runs on the detection thread and does not touch render state. It only writes `_pendingAlarm` while
  holding `_pendingGate` and uses `Interlocked.CompareExchange` to schedule one flush. Everything else (snapshot,
  `ShowAlarm`, `StateHasChanged`, the JS beep) runs inside `InvokeAsync(FlushAsync)` on the renderer's sync context.
  No component field is mutated from the foreign thread.
- Coalescing at 250 ms is correct. `_flushScheduled` is reset **before** `InvokeAsync`, so an event that arrives
  during a flush schedules a new one and no event is lost.
- The handler cannot throw: the `_disposed` check, the lock and the CAS are its only work. `FlushSoonAsync` catches
  OCE, ODE and `Exception`. If the circuit is torn down, `_cts.Token` on a disposed CTS throws ODE, which is caught.
  `InspectionLog.Add` also isolates each subscriber, so this is defence in depth.
- Unsubscribing in `DisposeAsync` happens before `_cts.Cancel()`, and `_disposed` is volatile, so the singleton
  holds no lingering delegate and the circuit does not leak. With global `InteractiveServer` + prerender,
  `OnInitialized` runs twice. The prerender instance is disposed by the HTML renderer at the end of the request,
  which unsubscribes it as well.
- The alarm hide timer uses a version counter, so an older timer cannot hide a newer alarm. `@key="_alarmVersion"`
  restarts the CSS animation.

**`RulesFileService`**
- Path is `ContentRootPath/rules.json`. This is the same file Program.cs watches through
  `PhysicalFileProvider(ContentRootPath)`.
- The temp file is in the same directory, then `File.Move(..., overwrite: true)` = `rename(2)` on Linux, which is
  atomic. Readers (config reload, `Read()`) never see a half-written file. Saves are serialised by `SemaphoreSlim`.
- Validation (trim, blank, case-insensitive duplicate, ≥1 rule) runs before anything is written, and the messages
  are German. Output is sorted, indented, and uses string verdicts. A test confirms that
  `ConfigurationBuilder` + bind gives the same rules.

**`PerMinute`**
- Minute flooring on `UtcTicks` handles offset timestamps correctly. The window `[now−(n−1), now]` includes both
  edges, empty minutes are returned as zero, the result is oldest first, and `Ignore` is not counted. `n ≤ 0` →
  empty result, no exception. All of this is computed under `_gate`.
- MS.DI resolves the optional `TimeProvider? time` parameter when it is registered, which Infrastructure does, so
  `FakeTimeProvider` is injectable.
- Tests cover all contract cases: buckets, empty minutes, edges, sliding window, UTC offsets.

**JS interop**
- The module is imported once in `OnAfterRenderAsync(firstRender)`. `JSDisconnectedException`, `JSException`,
  OCE and ODE are handled. Sound is off by default, and `enable()` is called from the click so the browser has a
  user gesture. `alarm.js` catches its own errors and blocks overlapping beep sequences.

---

## MAJOR

### M5-1 `RulesFileService.Read()` uses a stricter JSON parser than the config system → editor shows empty, Save wipes the rules
`RulesFileService.cs` `Read()`: `JsonDocument.Parse(stream)` uses the default `JsonDocumentOptions`, which reject
**comments and trailing commas**. The configuration JSON provider that loads the same file accepts both. If someone
hand-edits `rules.json` with a `// comment` or a trailing comma (quite likely during a live demo while explaining
the rules), these things happen together:
- The pipeline keeps working with the rules (config parses the file fine).
- `Read()` swallows the `JsonException` **silently** (no log, no UI hint) and returns an empty dictionary. `/regeln`
  shows an empty table while the "Aktiv: 5 Klassen" badge says otherwise.
- The user adds one rule and clicks "Speichern", which atomically overwrites the file with just that rule. All other
  rules are lost, and the provider applies the change because the count is not 0.

**Fix (≈5 lines):**
```csharp
private static readonly JsonDocumentOptions ReadOptions =
    new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
...
using JsonDocument doc = JsonDocument.Parse(stream, ReadOptions);
```
Also, either log the swallowed exception (inject `ILogger<RulesFileService>`) or return a "could not read" flag so
`Regeln.razor` can show a red message ("rules.json konnte nicht gelesen werden – Speichern überschreibt die Datei").
Suggested test: `Read_FileWithCommentsAndTrailingComma_ReturnsRules`.

---

## MINOR (brief)

1. **`ToggleSoundAsync` can kill the circuit** (`Home.razor:336`): it does not catch `TaskCanceledException` (JS
   interop timeout) or `ObjectDisposedException`. An unhandled exception in an `@onclick` handler tears down the
   circuit. Fix: add `catch (OperationCanceledException)`. Unlikely in practice.
2. **Late module import after dispose** (`Home.razor:187`): if `DisposeAsync` runs while `import` is still pending,
   the `IJSObjectReference` that arrives later is never disposed. This leaks only a small client-side reference.
   Fix: after the await, `if (_disposed) await module.DisposeAsync()`.
3. **`RefreshMetrics()` runs off the sync context** (`Home.razor:375`, pre-existing since M2/M4). It writes
   component fields outside `InvokeAsync` while a render may be in progress. All writes are reference/primitive
   assignments, so the worst case is one mixed frame of metrics. Fix: move it into the `InvokeAsync` lambda.
4. **Fire-and-forget tasks without a catch-all**: `HideAlarmLaterAsync` and `PollMetricsAsync` (and
   `Regeln.PollActiveAsync`) catch only OCE/ODE. Any other exception becomes an unobserved task exception with no
   log. Add `catch (Exception ex) { Logger.LogWarning(...) }`, as `FlushSoonAsync` already does.
5. **`SaveAsync` does not reject undefined/`Unknown` verdicts**: unlike `RuleSet`, it has no `Enum.IsDefined`
   check. The UI only offers defined values, so this is not reachable from the editor. The provider would reject an
   undefined value and keep the old rules, but the file would still be written. Add an `Enum.IsDefined` check
   (optionally reject `Unknown`) for symmetry with Core.
6. **Leftover `rules.json.tmp`** if `WriteAllTextAsync` throws or is cancelled midway. Harmless (it is not
   watched), but delete it in a `catch`.
7. **`PerMinute` is bounded by the 500-entry ring buffer.** Above ~33 events/min, older minutes in the 15-minute
   window silently show too few events. This is fine for the demo, and M6 (SQLite) is the natural fix. Document it
   in the XML doc or the learning note so it is explainable if asked.
8. **Beep/banner mismatch**: when a Nok arrives while a Danger banner is visible, the banner stays on Danger (by
   design) but the Nok beep still plays. Cosmetic.
9. **Doc drift**: `CLAUDE.md` (layout table + pipeline diagram) still mentions "SignalR `InspectionHub` (~4 Hz)".
   The M5 decision (recorded in PLAN.md) removed it. The orchestrator should update CLAUDE.md, because the reviewer
   may only write this file.
10. **`Read()` swallows non-string/unknown verdict entries silently**: the editor drops them and Save removes them
    from the file. This is acceptable, but it falls under the same "log it" recommendation as M5-1.

## Contract checklist

| Item | Status |
|---|---|
| `PerMinute` + `MinuteBucket` + injected `TimeProvider` | ✔ |
| `RulesFileService` singleton, validation, sorted/indented/string format, atomic tmp+move, `SemaphoreSlim` | ✔ (see M5-1 for Read) |
| Core / Infrastructure untouched | ✔ |
| Home: `EventAdded` push, `InvokeAsync`, unsubscribe in Dispose, 1 s timer only for metrics (+10 s chart slide) | ✔ |
| KPI row, GPU/CPU badge | ✔ |
| `PerMinuteChart` inline SVG, colours, HH:mm local every 3rd, y max ≥ 5, German legend | ✔ |
| Event log: 50 newest first, HH:mm:ss, German name, badge, confidence, fresh highlight | ✔ |
| Alarm banner 4 s (Nok red / Danger orange), `alarm.js` beeps 2×880 / 3×600, toggle default off | ✔ |
| `/regeln` editor: verdict select (no Unknown), add with datalist of 17 classes, delete, save, German messages, "Aktiv: n Klassen", nav links | ✔ |
| Tests: PerMinute with FakeTimeProvider; RulesFileService round-trip, validation, no `.tmp`, config bind | ✔ |
