# M2 contract — capture → channel → MJPEG

Shared by backend-dev, vision-engineer, frontend-dev and qa-engineer. **Do not change these signatures without the orchestrator.** Namespaces follow folders. Every file uses file-scoped namespaces. Code and comments are in English.

## Core (`src/QualityVision.Core`, zero packages) — owner: backend-dev

### `Frames/Frame.cs`
```csharp
namespace QualityVision.Core.Frames;

/// One captured image in BGRA32 (4 bytes per pixel, stride = Width * 4), backed by a pooled buffer.
/// The owner MUST dispose it; Dispose returns the buffer to the pool exactly once (idempotent, thread-safe).
public sealed class Frame : IDisposable
{
    public Frame(byte[] buffer, int width, int height, long sequence, DateTimeOffset capturedAt,
                 string sourceName, Action<byte[]>? returnBuffer = null);
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }                  // Width * 4
    public long Sequence { get; }
    public DateTimeOffset CapturedAt { get; }
    public string SourceName { get; }
    public ReadOnlySpan<byte> Pixels { get; }   // length Stride * Height; throws ObjectDisposedException after Dispose
    public byte[] UnsafeBuffer { get; }         // for interop (Mat/SKBitmap wrapping) — valid only until Dispose
    public void Dispose();
}
```
- Validate the arguments: width and height must be > 0, and `buffer.Length` must be >= `width * 4 * height`.
- `returnBuffer` is null in tests. In production it is `ArrayPool<byte>.Shared.Return`.

### `Frames/IFrameSource.cs`
```csharp
namespace QualityVision.Core.Frames;

public interface IFrameSource : IAsyncDisposable
{
    string Name { get; }   // e.g. "Phone", "Url: http://…", "VideoFile: demo.mp4"
    /// Yields frames until cancelled or the source fails/ends (then it completes or throws).
    /// The CONSUMER owns and disposes every yielded frame.
    IAsyncEnumerable<Frame> ReadFramesAsync(CancellationToken cancellationToken);
}
```

### `Geometry/NormalizedRect.cs`
```csharp
namespace QualityVision.Core.Geometry;
/// Rectangle in normalized image coordinates (0..1). Used for the Prüfzone (M2 drawing, M4 logic).
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y);   // inclusive edges
    public bool IsValid { get; }                // all in [0,1], Width/Height > 0, X+Width <= 1, Y+Height <= 1
}
```

## Infrastructure (`src/QualityVision.Infrastructure`) — owner: vision-engineer

Packages: `OpenCvSharp4` and `OpenCvSharp4.official.runtime.linux-x64` (versions are already in CPM). Use `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for logging, options and hosting abstractions, so no extra package versions are needed.

### `Camera/CameraOptions.cs` (section `"Camera"`)
```csharp
public sealed class CameraOptions
{
    public const string SectionName = "Camera";
    public CameraSourceKind Source { get; set; } = CameraSourceKind.Phone;
    public string? Url { get; set; }              // Source=Url, e.g. http://192.168.18.93:4747/video
    public int WebcamIndex { get; set; }          // Source=Webcam
    public string? VideoFile { get; set; }        // Source=VideoFile, path relative to content root allowed
    public int RequestedWidth { get; set; } = 1280;
    public int RequestedHeight { get; set; } = 720;
    public int MaxFps { get; set; } = 20;         // cap on frames produced per second
}
public enum CameraSourceKind { Phone, Url, Webcam, VideoFile }
```

### `Camera/OpenCvFrameSource.cs`
- Implements `IFrameSource` for Url (`VideoCaptureAPIs.FFMPEG`), Webcam (`V4L2`, requested size) and VideoFile (`FFMPEG`).
- VideoFile **loops**: on end it rewinds (`PosFrames = 0`) and paces playback to the file's FPS using `TimeProvider`.
- The blocking `Read` runs off the async context (`Task.Run` / `Task.Yield` per frame is acceptable).
- Converts BGR to BGRA into a buffer from `ArrayPool<byte>.Shared`, then builds a `Frame`.
- Every `Mat` sits inside a `using`.
- If it cannot open, or N=30 consecutive reads are empty on a live source, it throws `FrameSourceException`, which is defined in Core as `Frames/FrameSourceException.cs : Exception`. The owner of Core creates it.

### `Camera/PhoneFrameSource.cs` (singleton)
- `void Push(ReadOnlySpan<byte> jpeg)`:
  - decodes with `Cv2.ImDecode(..., ImreadModes.Color)`;
  - downscales so the longest side is ≤ `max(RequestedWidth, RequestedHeight)`;
  - converts to BGRA in a pooled buffer and writes the `Frame` to an internal bounded channel (capacity 1, `DropOldest`, the dropped frame is disposed through `itemDropped`).
  - Invalid JPEG → `false` from `bool TryPush(...)`. Use `TryPush` as the public API, not `Push`.
- `ReadFramesAsync` reads from that internal channel.
- `DateTimeOffset? LastFrameAt` (via `TimeProvider`).

### `Camera/FrameSourceFactory.cs`
- `IFrameSource Create()` returns the configured source from `IOptionsMonitor<CameraOptions>.CurrentValue`. For Phone it returns the singleton `PhoneFrameSource` wrapped so that disposing the wrapper does NOT dispose the singleton.

### `Imaging/JpegFrameEncoder.cs` (singleton)
- `byte[] Encode(Frame frame, NormalizedRect? zone, int quality, int maxWidth)`:
  - wraps the frame pixels in a Mat **without copying** (`Mat.FromPixelData` with BGRA, i.e. `MatType.CV_8UC4`);
  - draws the Prüfzone if given: dashed or solid rectangle 3 px thick, colour `#22c55e`, label "Prüfzone" with a dark background, readable when shared over Meet;
  - downsizes to `maxWidth` if larger, converts to BGR and encodes JPEG with `ImwriteFlags.JpegQuality`.
  - Every intermediate `Mat` sits inside a `using`.
  - M3 will extend this, or add a sibling, to draw detection boxes.

### `DependencyInjection.cs`
- `public static IServiceCollection AddQualityVisionInfrastructure(this IServiceCollection services, IConfiguration configuration)`:
  - binds `CameraOptions` with `ValidateDataAnnotations`-style checks done manually or with `Validate(...)`, plus `ValidateOnStart`;
  - registers `TimeProvider.System` (`TryAddSingleton`), `PhoneFrameSource`, `FrameSourceFactory` and `JpegFrameEncoder`.

## Web (`src/QualityVision.Web`) — owner: backend-dev

### Options
- `Pipeline/StreamOptions.cs`, section `"Stream"`: `MaxFps` = 15, `JpegQuality` = 75, `MaxWidth` = 1280.
- `Pipeline/InspectionOptions.cs`, section `"Inspection"`: `NormalizedRect Zone` = (0.3, 0.2, 0.4, 0.6).

### Pipeline
- **Frame channel:** register `Channel<Frame>` as a singleton: `Channel.CreateBounded<Frame>(new BoundedChannelOptions(2) { FullMode = DropOldest, SingleReader = true, SingleWriter = true }, itemDropped: f => f.Dispose())`.
- **`Pipeline/CameraStatus.cs`:** singleton, thread-safe, implements `ICameraStatus`:
  ```csharp
  public interface ICameraStatus
  {
      string SourceName { get; }
      bool IsConnected { get; }
      double FpsIn { get; }               // frames/s produced by the source, 1 s window
      DateTimeOffset? LastFrameAt { get; }
      string? LastError { get; }
  }
  ```
  Plus internal update methods.
- **`Pipeline/CameraService.cs` : `BackgroundService`:**
  - in a loop: `factory.Create()`, then `await foreach` over `ReadFramesAsync`, `TryWrite` each frame into the channel, and update `CameraStatus`;
  - on an exception or end of stream: log it, set the status to disconnected, dispose the source, then **reconnect with exponential backoff** (1 s, 2 s, 4 s … max 10 s, reset after success);
  - never crashes the host.
- **`Pipeline/FrameBroadcaster.cs`:** singleton, holds the latest encoded JPEG.
  ```csharp
  public void Publish(byte[] jpeg);      // increments the sequence, completes waiters
  public (byte[] Jpeg, long Sequence)? Latest { get; }
  public Task<(byte[] Jpeg, long Sequence)> WaitForNextAsync(long afterSequence, CancellationToken ct);
  // returns immediately if Latest.Sequence > afterSequence; otherwise awaits the next Publish
  // (TaskCompletionSource with RunContinuationsAsynchronously, swapped atomically)
  ```
- **`Pipeline/DetectionService.cs` : `BackgroundService`:**
  - reads the channel (`await foreach ReadAllAsync`) and handles each frame inside `using (frame)`;
  - in M2 it only throttles to `Stream:MaxFps` and calls `JpegFrameEncoder.Encode(frame, zone, quality, maxWidth)`, then `FrameBroadcaster.Publish`;
  - M3 inserts YOLO here. The class comment must say so.

### Endpoints (`Endpoints/CameraEndpoints.cs`, extension `MapCameraEndpoints`)
- `GET /stream`:
  - content type `multipart/x-mixed-replace; boundary=frame`, `Cache-Control: no-store`;
  - loops on `WaitForNextAsync` and writes each part (headers `Content-Type: image/jpeg` and `Content-Length`), then flushes;
  - ends quietly on `RequestAborted`;
  - disable response buffering through `IHttpResponseBodyFeature.DisableBuffering()`.
- `POST /api/camera/frame`:
  - accepts only `Content-Type: image/jpeg`, otherwise 415; body at most 2 MB (`[RequestSizeLimit]` or a manual check), otherwise 413;
  - 409 if `Camera:Source` is not `Phone`; reads the body into a pooled buffer;
  - `phoneSource.TryPush` → 204 on success, 400 on an invalid JPEG.
- `GET /api/camera/status` → JSON of `ICameraStatus`.
- `GET /kamera` → serves `wwwroot/kamera.html`. Owner: frontend-dev for the HTML; the route is mapped by backend-dev.

### Kestrel
- `appsettings.json`: `Kestrel:Endpoints:Http:Url = http://0.0.0.0:5080` (remove the old `Urls` key).
- An **optional** `appsettings.Local.json` (git-ignored, loaded with `optional: true` in `Program.cs`) adds `Kestrel:Endpoints:Https` on `https://0.0.0.0:5443` with `Certificate:Path`/`KeyPath` (PEM) pointing outside the repo. The orchestrator creates that file.
- `appsettings.json` also gets `Camera` (Source `Phone`, `Url` `http://192.168.18.93:4747/video`, `VideoFile` `samples/demo.mp4`), `Stream` and `Inspection` sections.

## Frontend (`src/QualityVision.Web`) — owner: frontend-dev
- **`wwwroot/kamera.html`:** a port of `spikes/phone-cam/index.html`.
  - POST to `/api/camera/frame`.
  - **Scale so the longest side ≤ the selected size** (portrait phones must not produce 1280x2276).
  - Show a German hint "Für beste Ergebnisse das Handy quer halten" in portrait.
  - Keep the one-request-in-flight backpressure, Wake Lock and the German errors.
  - Show a 409 response as "Kamera-Quelle ist nicht auf Handy eingestellt".
- **`Components/Pages/Home.razor`:** the live view `<img src="/stream">`, responsive, max 100% width, dark frame.
  - A status panel polled every 1 s from injected `ICameraStatus`, using `@implements IDisposable` and a `PeriodicTimer` or `System.Threading.Timer` with `InvokeAsync(StateHasChanged)`. It shows Quelle, Verbunden (green) / Getrennt (red), Eingangs-FPS and Letztes Bild vor X ms.
  - A placeholder line "Erkennung folgt in M3".
- High contrast and big type, legible when shared over Google Meet.

## Tests (`tests/QualityVision.Tests`) — owner: qa-engineer (after the implementation)
- `Frame`: returns the buffer exactly once with concurrent `Dispose`; `Pixels` throws after `Dispose`; argument validation.
- `NormalizedRect`: `Contains` at the edges; `IsValid`.
- `FrameBroadcaster`: an immediate return when newer; a waiter completes on `Publish`; cancellation.
- Bounded channel with `DropOldest` and `itemDropped`: the dropped frames get disposed (use real `Frame`s with a counting `returnBuffer`).
- `PhoneFrameSource`: a valid JPEG (generated with `Cv2.ImEncode` in the test) → a frame of the expected size; invalid bytes → `TryPush` returns false; portrait 720x1280 input with a 1280 limit → stays ≤ 1280 on the longest side.
