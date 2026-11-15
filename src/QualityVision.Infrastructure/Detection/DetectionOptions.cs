namespace QualityVision.Infrastructure.Detection;

/// <summary>Object detection configuration (section <c>"Detection"</c>). Validated at startup by <see cref="DetectionOptionsValidator"/>.</summary>
public sealed class DetectionOptions
{
    public const string SectionName = "Detection";

    /// <summary>ONNX model path; a relative path is resolved against <see cref="AppContext.BaseDirectory"/>.</summary>
    public string ModelPath { get; set; } = "models/yolo11n.onnx";

    /// <summary>Minimum confidence in (0, 1). Read per frame, so a config reload takes effect immediately.</summary>
    public double Confidence { get; set; } = 0.45;

    /// <summary>IoU threshold for non-maximum suppression in (0, 1). Read per frame.</summary>
    public double Iou { get; set; } = 0.7;

    /// <summary><see cref="ModelPath"/> as an absolute path (relative paths are resolved against the app base directory).</summary>
    public string ResolveModelPath() =>
        Path.IsPathRooted(ModelPath) ? ModelPath : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ModelPath));
}
