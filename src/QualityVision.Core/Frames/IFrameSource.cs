namespace QualityVision.Core.Frames;

/// <summary>A source of camera frames (Strategy): OpenCV (Url / Webcam / VideoFile) or the phone page.</summary>
public interface IFrameSource : IAsyncDisposable
{
    /// <summary>Human-readable source name, e.g. "Phone", "Url: http://…", "VideoFile: demo.mp4".</summary>
    string Name { get; }

    /// <summary>
    /// Yields frames until cancelled or the source fails/ends (then it completes or throws
    /// <see cref="FrameSourceException"/>). The CONSUMER owns and disposes every yielded frame.
    /// </summary>
    IAsyncEnumerable<Frame> ReadFramesAsync(CancellationToken cancellationToken);
}
