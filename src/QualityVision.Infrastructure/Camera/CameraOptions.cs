namespace QualityVision.Infrastructure.Camera;

/// <summary>Camera configuration (section <c>"Camera"</c>). Validated at startup in <c>DependencyInjection</c>.</summary>
public sealed class CameraOptions
{
    public const string SectionName = "Camera";

    /// <summary>Which frame source to use.</summary>
    public CameraSourceKind Source { get; set; } = CameraSourceKind.Phone;

    /// <summary>Stream URL for <see cref="CameraSourceKind.Url"/>, e.g. <c>http://192.168.18.93:4747/video</c>.</summary>
    public string? Url { get; set; }

    /// <summary>V4L2 device index for <see cref="CameraSourceKind.Webcam"/> (0 = <c>/dev/video0</c>).</summary>
    public int WebcamIndex { get; set; }

    /// <summary>Video file for <see cref="CameraSourceKind.VideoFile"/>; a relative path is resolved against the content root.</summary>
    public string? VideoFile { get; set; }

    /// <summary>Requested capture width. The longest side of every frame is capped at max(width, height).</summary>
    public int RequestedWidth { get; set; } = 1280;

    /// <summary>Requested capture height.</summary>
    public int RequestedHeight { get; set; } = 720;

    /// <summary>Upper bound on frames produced per second by the OpenCV sources.</summary>
    public int MaxFps { get; set; } = 20;

    /// <summary>
    /// If set, every raw captured frame is recorded to this mp4 (relative = content root) for the video Plan B.
    /// </summary>
    public string? RecordTo { get; set; }

    /// <summary>Maximum recording length in seconds of captured time.</summary>
    public int RecordMaxSeconds { get; set; } = 120;

    /// <summary>Longest side allowed for a produced frame.</summary>
    public int MaxSide => Math.Max(RequestedWidth, RequestedHeight);
}

/// <summary>Selects the <c>IFrameSource</c> implementation.</summary>
public enum CameraSourceKind
{
    /// <summary>JPEG frames pushed by the iPhone capture page (<c>POST /api/camera/frame</c>).</summary>
    Phone,

    /// <summary>MJPEG / RTSP stream (DroidCam) read with the OpenCV FFmpeg backend.</summary>
    Url,

    /// <summary>Local V4L2 webcam.</summary>
    Webcam,

    /// <summary>Looped video file (Plan B).</summary>
    VideoFile,
}
