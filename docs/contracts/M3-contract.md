# M3 contract — YOLO11n detection with drawn boxes

**Facts verified by the orchestrator in a scratch project:**
- **Model:** `models/yolo11n.onnx` is already exported (opset 17, static input `images` 1x3x640x640, output `output0` 1x84x8400, Ultralytics metadata included).
- **Inference speed:** YoloDotNet 4.2.0 with `YoloDotNet.ExecutionProvider.Cpu` 1.1.0 runs it on this PC at **~45 ms/inference**. On `bus.jpg` it returns bus 0.91 and person 0.89/0.84/0.83.
- **API (by reflection):**
  - `new Yolo(new YoloOptions { ExecutionProvider = new CpuExecutionProvider(string modelPath) })`
  - `Yolo : IDisposable`
  - `List<ObjectDetection> RunObjectDetection(SKBitmap img, double confidence = 0.2, double iou = 0.7, SKRectI? roi = null)`
  - `ObjectDetection` has `LabelModel Label` (`Index`, `Name`), `double Confidence`, `SKRectI BoundingBox` (pixel coordinates).
  - `YoloOptions` also has `ImageResize` (`Proportional` | `Stretched`); use `Proportional`.
- **Native assets:** `SkiaSharp.NativeAssets.Linux.NoDependencies` 3.119.1 is required on Linux. It is already pinned in CPM.
- **Font:** `/usr/share/fonts/TTF/DejaVuSans-Bold.ttf` exists on this PC. Copy it into the repo as an asset (DejaVu licence permits redistribution) and add `DejaVu-LICENSE.txt` next to it.

## Core (`src/QualityVision.Core`, zero packages) — owner: vision-engineer
```csharp
namespace QualityVision.Core.Detection;

/// One detected object. Box is normalized (0..1) relative to the frame, so it is resolution independent.
public sealed record Detection(string ClassName, double Confidence, NormalizedRect Box)
{
    public (double X, double Y) Center { get; }   // Box.X + Box.Width/2, Box.Y + Box.Height/2
}

public interface IObjectDetector
{
    /// Runs inference synchronously (CPU-bound). Must NOT keep a reference to the frame after returning.
    IReadOnlyList<Detection> Detect(Frame frame);
}

public static class GermanLabels
{
    /// German display name for a COCO class (e.g. "bottle" → "Flasche", "cup" → "Tasse",
    /// "scissors" → "Schere", "cell phone" → "Handy", "person" → "Person", "laptop" → "Laptop",
    /// "keyboard" → "Tastatur", "mouse" → "Maus", "book" → "Buch", "spoon" → "Löffel", "fork" → "Gabel",
    /// "knife" → "Messer", "remote" → "Fernbedienung", "wine glass" → "Weinglas", "banana" → "Banane",
    /// "apple" → "Apfel", "orange" → "Orange", "chair" → "Stuhl", "tv" → "Monitor", "clock" → "Uhr",
    /// "vase" → "Vase", "teddy bear" → "Teddy", "toothbrush" → "Zahnbürste"); falls back to the original name.
    public static string Get(string className);
}
```

## Infrastructure — owner: vision-engineer
- **Packages:** add `YoloDotNet`, `YoloDotNet.ExecutionProvider.Cpu`, `SkiaSharp.NativeAssets.Linux.NoDependencies` (versions in CPM; `SkiaSharp` is pinned transitively).
- **`Detection/DetectionOptions.cs`** (section `"Detection"`):

  | Property | Default |
  |---|---|
  | `ModelPath` | `"models/yolo11n.onnx"` |
  | `Confidence` | `0.45` |
  | `Iou` | `0.7` |

  Validation: the file exists at startup, and both thresholds are in (0, 1).
  - `ModelPath` is resolved relative to `AppContext.BaseDirectory` when it is not rooted. The Web project copies the model there.
- **`Detection/YoloObjectDetector.cs`** — `IObjectDetector, IDisposable`, **singleton**:
  - It creates `Yolo` once in the constructor. Log the load time and `ModelInfo`.
  - `Detect(Frame)`:
    - wraps the frame's BGRA pixels in an `SKBitmap` **without copying**: `SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque)` + `InstallPixels` with a pinned `GCHandle` (or `fixed`), released in `finally`;
    - calls `RunObjectDetection(bitmap, Confidence, Iou)`;
    - maps the results to `Detection`, normalizing each box by the frame width and height and clamping it to [0,1].
  - Thread-safety: guard `Detect` with a `lock`. There is only one caller (`DetectionService`), but be explicit.
  - `public void WarmUp()` runs one inference on a black 640x640 frame.
- **`Imaging/SkiaFrameRenderer.cs`** — singleton; **replaces `JpegFrameEncoder`** (delete it and its usages and tests):
  ```csharp
  byte[] RenderJpeg(Frame frame, IReadOnlyList<Detection> detections, NormalizedRect? zone, int quality, int maxWidth);
  ```
  - Wrap the frame in an `SKBitmap` (no copy, as above). Draw onto an `SKSurface`/`SKCanvas`, either sized to the output (scaled to `maxWidth`, aspect kept) or directly on a scaled copy.
  - **Prüfzone:** green `#22c55e` rectangle 3 px, label **"Prüfzone"** with a real umlaut, in the bundled font, on a dark rounded background.
  - **Each detection:**
    - box 3 px in cyan `#38bdf8` (M4 will colour by verdict, so pick the colour through a small `Func<Detection, SKColor>`-style hook or a parameter with this default);
    - label `"{GermanLabels.Get(ClassName)} {Confidence:P0}"` with the German culture → e.g. "Flasche 87 %";
    - text 18–22 px at 1280 width, bold, dark background, placed above the box (inside if there is no room).
  - Encode with `SKImage.Encode(SKEncodedImageFormat.Jpeg, quality)`.
  - Dispose every Skia object (`using`).
  - Load the `SKTypeface` **once** from the bundled TTF file (`SKTypeface.FromFile`). Do not rely on `SKTypeface.Default`: there is no fontconfig.
- **Font asset:** `src/QualityVision.Infrastructure/Assets/Fonts/DejaVuSans-Bold.ttf` + `DejaVu-LICENSE.txt`, both with `CopyToOutputDirectory=PreserveNewest` so they reach the Web output folder. Resolve them from `AppContext.BaseDirectory/Assets/Fonts/…`.
- **DI:** register `DetectionOptions` (validated, `ValidateOnStart`), `YoloObjectDetector` as both itself and `IObjectDetector` (same singleton instance), and `SkiaFrameRenderer`. Remove `JpegFrameEncoder`.

## Web — owner: backend-dev
- **`QualityVision.Web.csproj`:** link the model so it is copied to the output:
  `<None Include="..\..\models\yolo11n.onnx" Link="models\yolo11n.onnx" CopyToOutputDirectory="PreserveNewest" />`
- **`appsettings.json`:** add `"Detection": { "ModelPath": "models/yolo11n.onnx", "Confidence": 0.45, "Iou": 0.7 }`.
- **`Pipeline/PipelineMetrics.cs`** — singleton, thread-safe, implements:
  ```csharp
  public interface IPipelineMetrics
  {
      double ProcessedFps { get; }            // frames through detection per second (1 s window)
      double InferenceMs { get; }             // exponential moving average (alpha 0.2)
      double LastInferenceMs { get; }
      IReadOnlyList<Detection> LastDetections { get; }   // immutable snapshot of the latest frame
      long FramesProcessed { get; }
  }
  ```
- **`DetectionService`** (update), per frame inside `using (frame)`:
  1. `detector.Detect(frame)`, timed with `TimeProvider.GetTimestamp()` / `GetElapsedTime`;
  2. update the metrics;
  3. `renderer.RenderJpeg(frame, detections, zone, quality, maxWidth)`, then `broadcaster.Publish`.
  - Inference runs on every frame taken from the channel. The bounded channel with `DropOldest` absorbs a slower detector. Remove the old stream FPS throttle only if it would double-throttle; keep `Stream:MaxFps` as an upper bound.
  - Run the CPU-bound work with `await Task.Yield()` / keep it in the background loop. It must never block request threads.
  - Catch per-frame exceptions: log them (rate-limited, at most one log per 5 s) and continue. A bad frame must not kill the service.
- **Warm-up:** at startup (in `DetectionService.ExecuteAsync` before the loop, or a small hosted step) call `WarmUp()` and log `"Model warm-up took {Ms} ms"`.
- **Health check:** `ModelHealthCheck` (`IHealthCheck`). Healthy when the detector is constructed and at least one inference has succeeded (warm-up counts); otherwise Degraded. Register it with `AddCheck<ModelHealthCheck>("model")`. Keep `/health`.
- **Endpoint:** `GET /api/pipeline/metrics` → JSON of the `IPipelineMetrics` snapshot. Detections are listed as `{ className, label (German), confidence, box }`.

## Frontend — owner: frontend-dev
- **`Home.razor`:** add a metrics panel polled every 1 s from `IPipelineMetrics`:
  - "Verarbeitung" FPS, "Inferenz" ms (one decimal), "Erkannt" count;
  - a list of the current detections with German label and confidence %, sorted by confidence.
  - Large, high-contrast numbers that stay legible over Meet. Remove the "Erkennung folgt in M3" placeholder.
- Keep the layout responsive: the live image is the hero and the panels sit to the right on wide screens and below on narrow ones.

## Tooling + docs — owner: architect
- **`tools/export-model/export.py`:** a reproducible export, written as a PEP 723 inline-metadata script runnable with `uv run tools/export-model/export.py`. It pins Python 3.12 and `ultralytics`, `onnx`, `onnxslim` (use `>=` floors matching ultralytics 8.4, onnx 1.23).
  1. Download `yolo11n.pt`.
  2. Export to ONNX with `opset=17, dynamic=False, imgsz=640, simplify=True`.
  3. Verify the opset and the input shape with `onnx`, and print a summary.
  4. Copy the result to `models/yolo11n.onnx`.

  Plus `tools/export-model/README.md` in Spanish: why Python only here, how to run it, what to verify.
- **`docs/adr/ADR-003-object-detection-model.md`** (English, MADR):
  - why YOLO11n (size vs. speed: ~45 ms on 12 CPU threads), ONNX opset 17 static input, YoloDotNet vs. raw ONNX Runtime, CPU only;
  - **licensing:** Ultralytics YOLO11 weights/code are **AGPL-3.0**. Fine for this demo, but a commercial customer product would need an Ultralytics Enterprise licence or a permissively licensed model (e.g. RT-DETR / YOLOX, Apache-2.0). This is a strong interview point for a consultancy.
  - Add it to `docs/adr/README.md`.

## Tests — owner: qa-engineer (after the implementation)
- `GermanLabels`: the known mappings and the fallback.
- `Detection.Center`.
- **Integration (real model):** copy `bus.jpg` into `tests/QualityVision.Tests/Assets/bus.jpg`. Get it from `tools/export-model/.venv/lib/python3.12/site-packages/ultralytics/assets/bus.jpg`; the orchestrator copies it.
  - Decode it to a BGRA `Frame` via OpenCV.
  - `YoloObjectDetector.Detect` should return at least one `person` with ≥ 0.5 and one `bus` with ≥ 0.5, every box valid (`IsValid`).
  - The model file is linked into the test output the same way as in Web.
- `SkiaFrameRenderer`: returns a valid JPEG (starts with FF D8, decodes to the expected width after `maxWidth` scaling) with and without detections and zone.
- `PipelineMetrics`: the EMA and FPS window maths with `FakeTimeProvider`.
