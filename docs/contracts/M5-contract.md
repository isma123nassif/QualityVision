# M5 contract — Real-time dashboard, alarm and rules editor (time-boxed: interview today)

**Decision (orchestrator, 16-nov):** no separate SignalR hub. Blazor Interactive Server already keeps a SignalR
connection (the "circuit") per browser tab; components subscribe to `IInspectionLog.EventAdded` and push with
`InvokeAsync(StateHasChanged)`. One transport, no JS client, no duplicate serialization. (Recorded in PLAN.md
decision log by the orchestrator.) Chart = server-rendered inline SVG, no JS chart library.

## Backend — owner: backend-dev (Web project only)
1. `IInspectionLog` gains
   ```csharp
   /// Counts per verdict for each of the last `minutes` wall-clock minutes (oldest first, includes the current
   /// minute, empty minutes included with zero counts). Uses the injected TimeProvider.
   IReadOnlyList<MinuteBucket> PerMinute(int minutes = 15);
   ```
   `public readonly record struct MinuteBucket(DateTimeOffset Minute, int Ok, int Nok, int Danger, int Unknown)`
   (Minute = start of minute, UTC). Computed from the ring buffer under the lock. `InspectionLog` gets
   `TimeProvider` via its constructor (DI already registers `TimeProvider.System`; check Program.cs).
2. `Pipeline/RulesFileService.cs` (singleton, register in Program.cs): manages `rules.json` in the content root
   (`IHostEnvironment.ContentRootPath`, the same file Program.cs loads).
   ```csharp
   IReadOnlyDictionary<string, Verdict> Read();                       // current file contents
   Task SaveAsync(IReadOnlyDictionary<string, Verdict> rules, CancellationToken ct);
   ```
   - `SaveAsync` validates first (non-blank names, trimmed, no case-insensitive duplicates, at least one rule)
     and throws `ArgumentException` with a German-friendly message otherwise.
   - Writes `{ "Rules": { "Classes": { ... } } }` indented, verdict as string ("Ok", "Nok", ...), sorted by name.
   - Atomic write: write `rules.json.tmp` in the same directory then `File.Move(tmp, path, overwrite: true)`.
     Serialize concurrent saves with a `SemaphoreSlim`.
   - The existing reloadOnChange + `OptionsRuleSetProvider` pick the change up; nothing else to wire.
3. Do not touch Core or Infrastructure.

## Frontend — owner: frontend-dev
1. **`Home.razor` push instead of polling for events:** subscribe to `IInspectionLog.EventAdded` in
   `OnInitialized`, unsubscribe in `Dispose`; handler calls `InvokeAsync(...)` to refresh counts/recent/chart
   and `StateHasChanged`. Keep the existing 1 s timer only for metrics (FPS, ms, detections). Handlers must
   never throw (catch `ObjectDisposedException` when the circuit is gone).
2. **KPI row** (large, Meet-legible): i.O., n.i.O., Gefahr, Unbekannt, Ausschussquote %, plus FPS and Inferenz ms
   and the existing GPU/CPU badge. Keep the existing ones; reorganize only if needed.
3. **Chart "Prüfungen pro Minute"** (`Components/Shared/PerMinuteChart.razor`, parameter
   `IReadOnlyList<MinuteBucket> Buckets`): inline SVG stacked bars per minute (Ok green #22c55e, Nok red
   #ef4444, Danger orange #f97316, Unknown grey #9ca3af), x labels HH:mm (local time, every 3rd label),
   y max auto (at least 5), small legend in German. Data from `InspectionLog.PerMinute(15)`, refreshed on
   events and once per 10 s (so the window slides).
4. **Ereignisprotokoll:** last 50 events, newest first, time HH:mm:ss, German name, verdict badge, confidence.
   New entries briefly highlighted (CSS animation).
5. **Alarm on Nok and Danger:** full-width banner "ALARM: n.i.O. – Tasse erkannt" (red) / "GEFAHR – Schere
   erkannt" (orange), visible 4 s, and a sound via a tiny JS module `wwwroot/js/alarm.js` (Web Audio API
   oscillator beep: Nok two short 880 Hz beeps, Danger three 600 Hz beeps; no audio files). Browsers block
   audio until a user gesture: show a toggle button "🔔 Ton an/aus" (default off) that, when switched on,
   resumes/creates the AudioContext. Call it with `IJSRuntime` (`import` the module once,
   `OnAfterRenderAsync(firstRender)`); swallow `JSDisconnectedException`.
6. **Rules editor `/regeln`** (`Components/Pages/Regeln.razor`, `@rendermode InteractiveServer` if not global):
   table class (COCO name + German name from `GermanLabels.Get`) → `<select>` of verdicts with German labels
   (i.O., n.i.O., GEFAHR, Ignoriert; Unbekannt not offered since unlisted classes are Unknown), delete button,
   "Regel hinzufügen" row with a `<select>`/datalist of common COCO classes (bottle, cup, cell phone, scissors,
   person, book, mouse, keyboard, remote, banana, apple, spoon, fork, knife, wine glass, laptop, toothbrush) or
   free text, "Speichern" → `RulesFileService.SaveAsync`; success "Gespeichert – Regeln werden live übernommen",
   `ArgumentException` message shown in red. Show the currently ACTIVE rules from `IRuleSetProvider.Current`
   too ("Aktiv: 5 Klassen") to prove the hot reload. Nav link in the header: "Prüfstation" / "Regeln".
7. German UI, existing dark style, legible over Meet.

## Tests — owner: qa-engineer (after)
- `PerMinute` with `FakeTimeProvider`: buckets, empty minutes, window edges.
- `RulesFileService`: round-trip, validation errors, atomic write leaves no `.tmp`, file format readable by
  `ConfigurationBuilder().AddJsonFile` + bind to `RulesOptions` → same rules.
- bUnit is not in the project: no component tests.
