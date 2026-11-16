# ADR-004: GPU acceleration (CUDA) with automatic CPU fallback

- **Status:** Accepted
- **Date:** 2026-11-15
- **Deciders:** architect, orchestrator, project owner
- **Supersedes in part:** [ADR-003](ADR-003-object-detection-model.md) (decision 4, "CPU only") and the "no GPU" non-goal in [ADR-001](ADR-001-architecture.md) and `CLAUDE.md`

## Context

The project owner asked to use the full power of the demo PC (NVIDIA RTX 5070 Ti, 16 GB, driver 620.70,
CUDA 13.4, cuDNN 9.28) to get the best detection quality at the interview. ADR-003 chose YOLO11n on CPU
because the machine was assumed to have no usable GPU. That assumption no longer holds.

Measured on the demo PC (ONNX Runtime 1.32.0, opset 17, static 640):

| Model | CPU | CUDA |
|---|---|---|
| yolo11n | ~25-45 ms | 3.9 ms |
| yolo11s | ~50-85 ms | 4.3 ms |
| yolo11m | 198 ms | **6.4-6.9 ms** |

yolo11m is unusable on CPU (198 ms), but on CUDA it is far faster than yolo11n on CPU and clearly more
accurate. Inference stops being the pipeline bottleneck. The first CUDA load takes about 5.5 s (CUDA
context initialisation); later loads take 0.1-0.2 s.

## Decision

1. **Use yolo11m on CUDA** (`models/yolo11m.onnx`, 80.7 MB, same export settings as the n model).
2. **Automatic fallback to yolo11n on CPU.** `DetectionOptions.Device` is `Auto | Cuda | Cpu` (default `Auto`).
   - `Auto`: try CUDA with `ModelPath`; on any exception log a warning with the reason and the hint
     "install cuDNN 9 / CUDA 13 drivers", then use CPU with `CpuModelPath`.
   - `Cuda`: failure is fatal (the constructor throws). `Cpu`: CPU with `CpuModelPath`.
   - The detector exposes `ActiveDevice` and `ActiveModel`; they are logged at startup and shown in the
     health check, `/api/pipeline/metrics` and a dashboard badge, so the audience sees what runs.
3. **Package setup (verified):**
   - `YoloDotNet.ExecutionProvider.Cuda` 1.1.0 with `Microsoft.ML.OnnxRuntime.Gpu` 1.32.0 (built against CUDA 13).
   - `YoloDotNet.ExecutionProvider.Cpu` 1.1.0 for the fallback path.
   - `Microsoft.ML.OnnxRuntime` 1.32.0 is referenced with `ExcludeAssets="native;runtime"`. `ExcludeAssets` does
     **not** flow through `ProjectReference`, so every executable project (Web, Tests) must repeat
     `<PackageReference Include="Microsoft.ML.OnnxRuntime" ExcludeAssets="native;runtime" />`, not only
     Infrastructure. A guard test (`OnnxRuntimeNativeLibrary_InOutput_IsGpuBuild`) enforces this.
4. **Layering is unchanged.** Core stays free of ONNX/CUDA types; provider choice lives entirely in
   `YoloObjectDetector` (Infrastructure) behind `IObjectDetector`.

### Why `ExcludeAssets`

`Microsoft.ML.OnnxRuntime` (CPU) and `Microsoft.ML.OnnxRuntime.Gpu` both ship a native `libonnxruntime.so`
with the same file name. The CPU execution provider package pulls the CPU package transitively; if its
native asset reaches the output folder it overwrites the GPU build and CUDA fails to load. Excluding
`native;runtime` from the CPU package leaves only the GPU build, which also contains the CPU provider.
Result: `CpuExecutionProvider` and `CudaExecutionProvider` both work in the same process.

`ExcludeAssets` is a property of one project's package reference and does **not** flow through
`ProjectReference`. Excluding the assets only in Infrastructure is therefore not enough: Web and Tests still
received the CPU package transitively and copied its native library into their output. Every executable
project must repeat the `PackageReference` with `ExcludeAssets="native;runtime"`. This was found during
integration (CPU `libonnxruntime.so` of 34.8 MB in the output; after the fix the GPU library of 38.1 MB) and
is now guarded by the test `OnnxRuntimeNativeLibrary_InOutput_IsGpuBuild`, which asserts that
`OrtEnv.Instance().GetAvailableProviders()` contains `CUDAExecutionProvider`. It needs no GPU, because the
GPU build lists the provider even on machines without one.

## Consequences

- Good: ~6.5 ms inference with a much stronger model; headroom for `Stream:MaxFps` 20.
- Good: the app still runs on machines without a GPU (slower, yolo11n), so tests stay green everywhere.
- Bad: the demo PC depends on the NVIDIA driver, CUDA 13 and cuDNN 9 (system packages `cuda`, `cudnn`,
  installed with pacman on 2026-11-15). If a library is missing, `Auto` degrades to CPU with a logged warning.
- Bad: ~5.5 s extra start-up on the first CUDA load; start the app before the demo.
- Bad: the repository grows by an 80.7 MB model file (see `models/README.md`), and the GPU native
  libraries enlarge the build output.
- Bad: the packaging trick is subtle; it stays documented in the csproj comment and here.
- Neutral: yolo11m is AGPL-3.0 like yolo11n; the licensing note in ADR-003 applies unchanged.
- Neutral: debounce is measured in frames (ADR-002) and FPS rises, so the frame count may need re-tuning.

## Alternatives considered

- **Stay on CPU with yolo11n (ADR-003):** simplest, no system dependencies, but lower accuracy and the
  hardware stays unused.
- **yolo11s on CUDA:** 4.3 ms, but less accurate than m at almost the same cost.
- **yolo11m on CPU:** 198 ms per frame, below 5 FPS; not interactive.
- **TensorRT provider:** faster, but long engine builds and more driver risk; 6.5 ms is already enough.
- **DirectML / OpenVINO:** not applicable to an NVIDIA GPU on Linux.
- **GPU only, no fallback:** brittle on demo day if a driver library is missing.
