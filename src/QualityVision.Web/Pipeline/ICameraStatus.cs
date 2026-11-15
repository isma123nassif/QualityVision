namespace QualityVision.Web.Pipeline;

/// <summary>Read-only view of the camera health, shown on the dashboard and at <c>GET /api/camera/status</c>.</summary>
public interface ICameraStatus
{
    string SourceName { get; }

    /// <summary>True while the source delivers frames (last frame younger than 2 s).</summary>
    bool IsConnected { get; }

    /// <summary>Frames per second produced by the source, measured over a 1 s window.</summary>
    double FpsIn { get; }

    DateTimeOffset? LastFrameAt { get; }

    string? LastError { get; }
}
