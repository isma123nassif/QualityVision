namespace QualityVision.Web.Pipeline;

/// <summary>
/// Thread-safe camera health. Written only by <see cref="CameraService"/> (internal update methods),
/// read by the UI and the status endpoint. A single lock keeps the fields consistent with each other.
/// </summary>
public sealed class CameraStatus(TimeProvider timeProvider) : ICameraStatus
{
    private static readonly TimeSpan FpsWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private string _sourceName = "-";
    private bool _isConnected;
    private double _fpsIn;
    private DateTimeOffset? _lastFrameAt;
    private string? _lastError;
    private long _windowStart;
    private int _windowFrames;

    public string SourceName
    {
        get
        {
            lock (_gate)
            {
                return _sourceName;
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            lock (_gate)
            {
                return _isConnected && IsFresh(now);
            }
        }
    }

    public double FpsIn
    {
        get
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            lock (_gate)
            {
                return _isConnected && IsFresh(now) ? _fpsIn : 0;
            }
        }
    }

    public DateTimeOffset? LastFrameAt
    {
        get
        {
            lock (_gate)
            {
                return _lastFrameAt;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    /// <summary>A new source is being opened; not connected until its first frame arrives.</summary>
    internal void ReportSourceOpening(string sourceName)
    {
        lock (_gate)
        {
            _sourceName = sourceName;
            _isConnected = false;
            _fpsIn = 0;
        }
    }

    /// <summary>Called for every frame produced by the source.</summary>
    internal void ReportFrame()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        long timestamp = timeProvider.GetTimestamp();
        lock (_gate)
        {
            _lastFrameAt = now;
            _lastError = null;

            if (!_isConnected)
            {
                // First frame after (re)connect: start a fresh FPS window.
                _isConnected = true;
                _fpsIn = 0;
                _windowStart = timestamp;
                _windowFrames = 0;
                return;
            }

            _windowFrames++;
            TimeSpan elapsed = timeProvider.GetElapsedTime(_windowStart, timestamp);
            if (elapsed >= FpsWindow)
            {
                _fpsIn = _windowFrames / elapsed.TotalSeconds;
                _windowStart = timestamp;
                _windowFrames = 0;
            }
        }
    }

    internal void ReportDisconnected(string? error)
    {
        lock (_gate)
        {
            _isConnected = false;
            _fpsIn = 0;
            _lastError = error;
        }
    }

    // Caller holds _gate.
    private bool IsFresh(DateTimeOffset now) =>
        _lastFrameAt is { } last && now - last <= StaleAfter;
}
