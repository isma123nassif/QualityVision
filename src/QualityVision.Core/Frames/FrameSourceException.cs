namespace QualityVision.Core.Frames;

/// <summary>Thrown by an <see cref="IFrameSource"/> that cannot open or has lost its camera/stream.</summary>
public sealed class FrameSourceException : Exception
{
    public FrameSourceException()
    {
    }

    public FrameSourceException(string message)
        : base(message)
    {
    }

    public FrameSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
