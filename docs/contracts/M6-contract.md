# M6 contract (reduced, interview today) — SQLite persistence, CSV export, recording for the video Plan B

## Backend — owner: backend-dev
- **Packages:** `Microsoft.EntityFrameworkCore.Sqlite` (already in CPM) in Infrastructure.
- **Infrastructure `Persistence/`:**
  - `InspectionDbContext` with `DbSet<InspectionRecord>`.
  - `InspectionRecord` (Id long, Timestamp DateTimeOffset stored as UTC ticks or ISO string — SQLite cannot order DateTimeOffset natively, use a ValueConverter to `long` UtcTicks; ClassName, DisplayName, Verdict as string, Confidence). Index on Timestamp.
  - No migrations: `Database.EnsureCreatedAsync()` at startup (documented trade-off: demo, single table; production would use migrations).
- **Connection string:** `ConnectionStrings:Inspections = "Data Source=data/qualityvision.db"`, relative to the content root (create the `data` directory). `data/` is gitignored.
- **Web `Pipeline/EventPersistenceService` (BackgroundService):**
  - subscribes to `IInspectionLog.EventAdded` and writes into a bounded `Channel<InspectionEvent>` (capacity 1000, DropOldest, never blocks the detection thread);
  - batches inserts (up to 50 events or 1 s) using `IDbContextFactory<InspectionDbContext>`;
  - logs errors and continues;
  - flushes on shutdown.
- **Counters survive restarts:** at startup, after `EnsureCreated`, load today's counts per verdict (local day via `TimeProvider`) and seed `InspectionLog`. Add `void Seed(InspectionCounts counts, IReadOnlyList<InspectionEvent> recent)`, called before the pipeline starts; recent = last 50 of today. Make the hosted-service order explicit, or do it in an `IHostedService.StartAsync` registered before `DetectionService`.
- **`GET /api/inspection/export.csv?from=&to=`:**
  - optional ISO dates; default today;
  - streams `text/csv; charset=utf-8` with BOM (Excel);
  - `;` separator (German Excel);
  - header `Zeitpunkt;Klasse;Bezeichnung;Bewertung;Konfidenz`;
  - local time `yyyy-MM-dd HH:mm:ss`, German verdict label, confidence `0,00`;
  - proper CSV escaping;
  - filename `pruefungen-YYYYMMDD.csv` via Content-Disposition.
- **Health:** add an EF `DbContext` check (`AddDbContextCheck`) if the package is available; otherwise a simple custom check `CanConnectAsync`.
- **Home.razor:** a button "CSV exportieren" (link to the endpoint) near the Ereignisprotokoll. Keep the markup minimal.

## Vision — owner: vision-engineer
- **`CameraOptions.RecordTo`** (string?, default null), plus `RecordMaxSeconds` (default 120).
  - When set, `CameraService` (or a small `FrameRecorder` in Infrastructure using OpenCvSharp `VideoWriter`, mp4v FourCC, at a fixed 15 fps) writes every **raw** captured frame (BGRA→BGR) to that file. The recording starts on the first frame and uses the first frame's size; frames of a different size are resized.
  - It stops after `RecordMaxSeconds` or at shutdown, and logs `"Recorded {Frames} frames to {Path}"`.
  - Recording must never break capture: catch, log once, disable.
  - The frame is still disposed by its owner: the recorder must copy it or finish writing before the frame is passed on.
- **Plan B:** `Camera:Source=VideoFile` + `Camera:VideoFile=samples/demo.mp4` already exist. Verify that the relative path resolves against the content root in the published layout too: run-demo.sh runs from `artifacts/demo`, so document it and/or let `run-demo.sh` accept `--video` to start with `Camera__Source=VideoFile` and `Camera__VideoFile=<repo>/samples/demo.mp4` (absolute).
- Add `samples/README.md` (Spanish): how to record (`Camera__RecordTo=samples/demo.mp4`) and how to play it back (`./run-demo.sh --video`).
- `samples/*.mp4` is NOT committed if > 50 MB; otherwise it is fine to commit.

## Tests — owner: qa-engineer (after)
- CSV formatting/escaping (pure function).
- `EventPersistenceService` with in-memory SQLite (`Data Source=:memory:` with an open connection): events are written; seed loads today's counts.
- `FrameRecorder` writes a readable file (OpenCV reads back N frames).
