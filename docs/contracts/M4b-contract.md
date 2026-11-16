# M4b contract — GPU acceleration (CUDA) with automatic CPU fallback

**Decision by the user (2026-11-15):** use the full GPU power of the demo PC. The orchestrator measured the following on the RTX 5070 Ti (16 GB) with CUDA 13.4 and cuDNN 9.28. The scratch projects are not in the repo.

| Model | CPU | CUDA |
|---|---|---|
| yolo11n | ~25–45 ms | 3.9 ms |
| yolo11s | ~50–85 ms | 4.3 ms |
| yolo11m | 198 ms | **6.4–6.9 ms** |

- The first CUDA load takes ~5.5 s (CUDA context init); later loads take ~0.1–0.2 s.
- **Working package setup, verified:**
  - `YoloDotNet.ExecutionProvider.Cuda` 1.1.0 together with `Microsoft.ML.OnnxRuntime.Gpu` **1.32.0** (built against CUDA 13: links `libcudart.so.13`/`libcublas.so.13`, and finds `libcudnn.so.9` at runtime).
  - **plus** `YoloDotNet.ExecutionProvider.Cpu` 1.1.0, with `Microsoft.ML.OnnxRuntime` 1.32.0 referenced using `ExcludeAssets="native;runtime"`, so that only the GPU build of `libonnxruntime.so` reaches the output. The GPU build also contains the CPU provider.
  - With this setup **both** `CpuExecutionProvider` and `CudaExecutionProvider` work in the same process.
  - Without `ExcludeAssets`, the CPU native library overwrites the GPU one and CUDA fails.
- **API:** `new CudaExecutionProvider(string model, int gpuId = 0, TensorRt? trtConfig = null)`. If CUDA or cuDNN is missing, the **constructor** throws an `OnnxRuntimeException`.

## Packages (CPM `Directory.Packages.props`) — owner: vision-engineer
- `Microsoft.ML.OnnxRuntime`: 1.23.0 → **1.32.0** (pinned transitive).
- Add `Microsoft.ML.OnnxRuntime.Gpu` 1.32.0 and `YoloDotNet.ExecutionProvider.Cuda` 1.1.0.
- Infrastructure csproj: add `YoloDotNet.ExecutionProvider.Cuda`, `Microsoft.ML.OnnxRuntime.Gpu`, and `<PackageReference Include="Microsoft.ML.OnnxRuntime" ExcludeAssets="native;runtime" />` with an XML comment explaining why. Do not use `--` inside XML comments.
- Check that the Tests project still gets the right native assets transitively.

## Infrastructure — owner: vision-engineer
**`DetectionOptions`:**

| Property | Default | Notes |
|---|---|---|
| `Device` | `DetectionDevice.Auto` | `enum DetectionDevice { Auto, Cuda, Cpu }` |
| `GpuId` | `0` | |
| `ModelPath` | `"models/yolo11m.onnx"` | model used on the GPU |
| `CpuModelPath` | `"models/yolo11n.onnx"` | model used on the CPU (fallback or forced) |
| `Confidence` | `0.45` | |
| `Iou` | `0.7` | |

The validator checks that both files exist.

**`YoloObjectDetector`** selects the provider in its constructor:
- `Cuda`: CUDA with `ModelPath`. A failure is fatal and the constructor throws.
- `Cpu`: CPU with `CpuModelPath`.
- `Auto`: try CUDA with `ModelPath`. On any exception, log a **warning** with the reason and the hint "install cuDNN 9 / CUDA 13 drivers", then use CPU with `CpuModelPath`.
- It exposes `public string ActiveDevice { get; }`, either `"GPU (CUDA {GpuId})"` or `"CPU"`, and `public string ActiveModel { get; }` with the file name, e.g. `yolo11m.onnx`. Log both at startup.

Keep everything else: the lock, zero-copy pinning, `WarmUp` and the normalisation.

## Web — owner: backend-dev
- **Csproj:** also link `..\..\models\yolo11m.onnx` to `models\yolo11m.onnx`. Keep the n model.
- **`appsettings.json`:**
  - `"Detection": { "Device": "Auto", "GpuId": 0, "ModelPath": "models/yolo11m.onnx", "CpuModelPath": "models/yolo11n.onnx", "Confidence": 0.45, "Iou": 0.7 }`
  - `Stream:MaxFps` → 20 (inference is no longer the bottleneck).
- **`IPipelineMetrics`:** add `string Device` and `string Model`, set once from the detector. Add them to `/api/pipeline/metrics`.
- **`ModelHealthCheck` description:** include the device and the model, e.g. "GPU (CUDA 0) · yolo11m.onnx".
- **Tests csproj:** link `yolo11m.onnx` too, the same way as the n model.

## Frontend — owner: frontend-dev
In `Home.razor` (metrics panel), add a small badge showing the device and the model:
- green "GPU · yolo11m" when CUDA is active;
- amber "CPU · yolo11n" when on CPU.

Legible over Meet.

## Docs/tooling — owner: architect
- **`docs/adr/ADR-004-gpu-acceleration.md`** (MADR, Accepted, 2026-11-15):
  - context: the user asked for maximum detection quality at the interview; the measured table; the decision to use yolo11m on CUDA with automatic fallback to yolo11n on CPU;
  - the ExcludeAssets trick and why it is needed;
  - consequences: depends on NVIDIA driver, CUDA 13 and cuDNN 9 on the demo PC (system packages `cuda`, `cudnn`); ~5.5 s first load; repo grows by an 80 MB model;
  - it supersedes the "no GPU" non-goal.
- Update the index, the `CLAUDE.md` non-goals line (remove GPU from it and reference ADR-004) and the `docs/PLAN.md` decision log (one row).
- **`models/README.md`:** add `yolo11m.onnx` with its provenance. The orchestrator fills in the SHA (leave the placeholder `SHA256: <fill-m>`).
- **`tools/export-model/export.py`:** add a `--size n|s|m` argument (default `n`) that exports `yolo11{size}` to `models/yolo11{size}.onnx`. Update the README.

## Tests — owner: qa-engineer (after the implementation)
- `DetectionOptions` validation with the new fields.
- **Forced Cpu:** `ActiveDevice == "CPU"`, `ActiveModel == "yolo11n.onnx"`, and `bus.jpg` is detected.
- **Auto:** detects `bus.jpg` and reports a non-empty device and model. On a machine without a GPU it falls back to CPU, so the test must pass in both cases.
- **Forced Cuda:** marked `[Trait("Category","Gpu")]`. It runs only if CUDA is available: wrap it in `Skip.If`-style logic, or attempt the construction and use `Assert.Skip` with xUnit v2-compatible means. If skipping isn't possible in xUnit v2, make it conditional with an early return and a clear test name.
