# QualityVision — project guide for Claude

Industrial visual quality inspection demo in C#/.NET 10. A live camera feed is shown in the browser with an
inspection zone ("Prüfzone"); YOLO11n (ONNX, CPU, YoloDotNet) detects objects, a configurable rules engine
assigns a verdict (OK / NOK / DANGER / UNKNOWN / IGNORE), objects are counted once when they *enter* the zone,
and a German Blazor dashboard shows KPIs, a per-minute chart and a live event log. Events are persisted to
SQLite and exportable as CSV. It is a weekend demo for a technical interview: **robust and explainable beats clever.**

Sources of truth: `docs/00-briefing/PROMPT_ORIGINAL.md` (scope), `docs/PLAN.md` (milestones, decisions),
`docs/adr/` (architecture decisions; start with ADR-001).

## Solution layout

| Project | Contents | References |
|---|---|---|
| `src/QualityVision.Core` | Domain: `Frame`, `Detection`, `Verdict`, `RuleSet`, `RulesEngine`, `InspectionZone`, `ZoneEntryTracker`, ports (`IFrameSource`, `IObjectDetector`, `IRuleSetProvider`) | **nothing** (zero NuGet packages) |
| `src/QualityVision.Infrastructure` | Frame sources (`OpenCvFrameSource`, `PhoneFrameSource`), YoloDotNet detector, `OptionsRuleSetProvider` (the only `IOptionsMonitor` user), EF Core + SQLite | Core |
| `src/QualityVision.Web` | Blazor Server (Interactive Server), SignalR `InspectionHub`, `/stream` MJPEG, minimal API endpoints, hosted services wiring | Infrastructure, Core |
| `tests/QualityVision.Tests` | xUnit unit tests + ArchitectureTests | Core, Infrastructure |

**Dependency rule:** Web → Infrastructure → Core. Core has zero package dependencies and never knows that
YOLO, OpenCV, EF Core or `Microsoft.Extensions.*` exist; Infrastructure translates into Core types.
Enforced by `ArchitectureTests`.

## Pipeline

```
IFrameSource: OpenCvFrameSource (Url | Webcam | VideoFile) | PhoneFrameSource (Phone)
  → CameraService (BackgroundService) → Channel<Frame> (cap 2, DropOldest, dispose dropped)
  → DetectionService (YOLO singleton) → RulesEngine + ZoneEntryTracker (Core)
  → FrameBroadcaster (latest annotated JPEG, encoded once) → GET /stream
  → SignalR InspectionHub (~4 Hz) → dashboard
  → Channel<InspectionEvent> → EventPersistenceService → SQLite
```

- Camera selected by `Camera:Source = Phone | Url | Webcam | VideoFile` (both sources in M2):
  `OpenCvFrameSource` handles `Url` (DroidCam MJPEG via the FFmpeg backend — default live camera),
  `Webcam` (V4L2 index) and `VideoFile` (looped, Plan B); `PhoneFrameSource` receives JPEGs from the iPhone
  page `/kamera` via `POST /api/camera/frame` (HTTPS endpoint, self-signed cert).
- If the live source is lost: reconnect with backoff; automatic fallback to the video file + UI banner in M6.
- Rules live in `rules.json` (`reloadOnChange`). `OptionsRuleSetProvider` (Infrastructure) maps
  `IOptionsMonitor<RulesOptions>` to an immutable Core `RuleSet`; `DetectionService` reads
  `IRuleSetProvider.Current` once per frame and calls the pure `RulesEngine.Evaluate(Detection, RuleSet)`.
  The web editor writes only `rules.json`. See ADR-002.
- Debounce counts **frames of absence** (`Inspection:AbsenceFrames`, default 5); depends on FPS by design (ADR-002).

Kestrel via `Kestrel:Endpoints`: HTTP `0.0.0.0:5080` (dashboard, `/stream`) and HTTPS `0.0.0.0:5443` (phone
capture page + frame endpoint). Certificate paths come from config/env and point **outside the repo**; never
commit certs or keys.

Demo topology: app runs on the Linux PC in the **Production** environment (`run-demo.sh`); the Windows laptop
joins Google Meet and shares a Chrome **tab** showing `http://<PC-LAN-IP>:5080`. LAN-only, no auth by design;
firewall limits the ports to the laptop/iPhone IPs (ADR-001 "Security considerations").
See `docs/investigacion/demo-por-meet.md`.

## Conventions

- File-scoped namespaces, `Nullable` enabled, `TreatWarningsAsErrors`, `ImplicitUsings` (set in `Directory.Build.props`).
- Central Package Management: versions only in `Directory.Packages.props`. **Never put `Version` in a `.csproj`.**
- Async all the way; every async method takes and honours a `CancellationToken`. No `.Result` / `.Wait()`.
- Strict disposal: every `Mat`, `SKBitmap` and `Frame` lives in a `using`; ownership transfer is explicit and commented.
- Hot-reloadable options via `IOptionsMonitor<T>` (never `IOptions<T>`), **only in Infrastructure/Web adapters**.
  Core never references `Microsoft.Extensions.*`; it receives plain values or immutable snapshots (`RuleSet`).
- Inject `TimeProvider` wherever wall-clock time matters; never `DateTime.Now` / `DateTime.UtcNow` in product
  code. Tests use `FakeTimeProvider` only where time matters (the frame-based debounce needs no clock).
- Structured logging with message templates (`logger.LogInformation("Frame {Index}", i)`), no string interpolation.
- Language: UI text **German**; code, identifiers, comments, commit messages **English**;
  `docs/aprendizaje/*.md` **Spanish**; interview kit `docs/entrevista/*_DE.md` German.
- Conventional Commits, one commit per milestone (`feat(m2): ...`).

## Commands

```bash
dotnet build -warnaserror
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/QualityVision.Web          # development (launch profile = Development)
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/QualityVision.Web --no-launch-profile   # demo
```

## Definition of Done (per milestone)

1. `dotnet build -warnaserror` → 0 warnings, 0 errors
2. `dotnet test` → green
3. `dotnet format --verify-no-changes` → clean
4. `code-reviewer` report without BLOCKER findings (minor ones noted), saved as `docs/reviews/MX-review.md`
5. Real functional verification (browser / curl / screenshot)
6. `docs/aprendizaje/MX.md` written in Spanish
7. Conventional Commit (`feat(m2): ...`)
8. **STOP → wait for the user's OK**

## Roles

Agents run as Claude CLI sessions in visible terminals, launched by the orchestrator (main session).
Each task brief includes the role definition from `.claude/agents/<role>.md`.

| Role | File | Owns |
|---|---|---|
| Orchestrator | main session | Plan, delegate, integrate, quality gates, git |
| architect | `.claude/agents/architect.md` | Structure, layer contracts, ADRs |
| backend-dev | `.claude/agents/backend-dev.md` | Pipeline, DI, hosted services, EF Core, endpoints |
| vision-engineer | `.claude/agents/vision-engineer.md` | Frame sources, ONNX model, detection, performance |
| frontend-dev | `.claude/agents/frontend-dev.md` | Blazor UI, dashboard, SignalR client, German UX |
| qa-engineer | `.claude/agents/qa-engineer.md` | xUnit tests, verification |
| code-reviewer | `.claude/agents/code-reviewer.md` | Review report; read-only except it may create **only** `docs/reviews/MX-review.md` |
| tech-writer | `.claude/agents/tech-writer.md` | README, learning notes, interview kit |

## No over-engineering

Not in scope: MediatR, CQRS, Docker, authentication, GPU, microservices, a custom object tracker,
generic repositories. If something is not needed to make Monday's demo work and be explainable, skip it.
