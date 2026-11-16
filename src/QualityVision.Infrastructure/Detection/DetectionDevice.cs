namespace QualityVision.Infrastructure.Detection;

/// <summary>Which ONNX Runtime execution provider <see cref="YoloObjectDetector"/> uses (ADR-004).</summary>
public enum DetectionDevice
{
    /// <summary>Try CUDA with <see cref="DetectionOptions.ModelPath"/>; on any failure fall back to CPU with <see cref="DetectionOptions.CpuModelPath"/>.</summary>
    Auto,

    /// <summary>CUDA only, with <see cref="DetectionOptions.ModelPath"/>. A failure to initialise is fatal.</summary>
    Cuda,

    /// <summary>CPU only, with <see cref="DetectionOptions.CpuModelPath"/>.</summary>
    Cpu,
}
