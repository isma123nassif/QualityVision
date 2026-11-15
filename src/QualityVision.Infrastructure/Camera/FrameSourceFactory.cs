using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using QualityVision.Core.Frames;

namespace QualityVision.Infrastructure.Camera;

/// <summary>
/// Creates the <see cref="IFrameSource"/> selected by <c>Camera:Source</c>, reading the options on every call so
/// a reconnect picks up configuration changes. The caller owns (and disposes) the returned source.
/// </summary>
public sealed class FrameSourceFactory
{
    private readonly IOptionsMonitor<CameraOptions> _options;
    private readonly PhoneFrameSource _phoneSource;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;

    public FrameSourceFactory(IOptionsMonitor<CameraOptions> options, PhoneFrameSource phoneSource,
        IHostEnvironment environment, TimeProvider timeProvider, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(phoneSource);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _options = options;
        _phoneSource = phoneSource;
        _environment = environment;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
    }

    public IFrameSource Create()
    {
        CameraOptions options = _options.CurrentValue;
        return options.Source switch
        {
            // The phone source is a DI singleton shared with the HTTP endpoint: hand out a wrapper whose
            // DisposeAsync does nothing, so the camera loop disposing "its" source never kills the singleton.
            CameraSourceKind.Phone => new NonDisposingFrameSource(_phoneSource),
            _ => new OpenCvFrameSource(
                options, _environment.ContentRootPath, _timeProvider, _loggerFactory.CreateLogger<OpenCvFrameSource>()),
        };
    }

    private sealed class NonDisposingFrameSource(IFrameSource inner) : IFrameSource
    {
        public string Name => inner.Name;

        public IAsyncEnumerable<Frame> ReadFramesAsync(CancellationToken cancellationToken) =>
            inner.ReadFramesAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
