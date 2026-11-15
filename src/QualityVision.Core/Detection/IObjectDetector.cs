using QualityVision.Core.Frames;

namespace QualityVision.Core.Detection;

/// <summary>Port for the object detector (implemented in Infrastructure with YoloDotNet).</summary>
public interface IObjectDetector
{
    /// <summary>
    /// Runs inference synchronously (CPU-bound). The caller keeps ownership of <paramref name="frame"/>;
    /// the detector must NOT keep a reference to the frame or its pixels after returning.
    /// </summary>
    IReadOnlyList<Detection> Detect(Frame frame);
}
