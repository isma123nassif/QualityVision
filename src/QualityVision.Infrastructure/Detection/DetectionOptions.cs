namespace QualityVision.Infrastructure.Detection;

/// <summary>Object detection configuration (section <c>"Detection"</c>). Validated at startup by <see cref="DetectionOptionsValidator"/>.</summary>
public sealed class DetectionOptions
{
    public const string SectionName = "Detection";

    /// <summary>Execution provider selection (ADR-004). Read once at startup.</summary>
    public DetectionDevice Device { get; set; } = DetectionDevice.Auto;

    /// <summary>CUDA device index used when running on the GPU. Read once at startup.</summary>
    public int GpuId { get; set; }

    /// <summary>ONNX model used on the GPU; a relative path is resolved against <see cref="AppContext.BaseDirectory"/>.</summary>
    public string ModelPath { get; set; } = "models/yolo11m.onnx";

    /// <summary>ONNX model used on the CPU (forced or as fallback); a relative path is resolved like <see cref="ModelPath"/>.</summary>
    public string CpuModelPath { get; set; } = "models/yolo11n.onnx";

    /// <summary>Minimum confidence in (0, 1). Read per frame, so a config reload takes effect immediately.</summary>
    public double Confidence { get; set; } = 0.45;

    /// <summary>IoU threshold for non-maximum suppression in (0, 1). Read per frame.</summary>
    public double Iou { get; set; } = 0.7;

    /// <summary><see cref="ModelPath"/> as an absolute path (relative paths are resolved against the app base directory).</summary>
    public string ResolveModelPath() => Resolve(ModelPath);

    /// <summary><see cref="CpuModelPath"/> as an absolute path (relative paths are resolved against the app base directory).</summary>
    public string ResolveCpuModelPath() => Resolve(CpuModelPath);

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
