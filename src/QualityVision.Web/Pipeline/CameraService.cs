using System.Threading.Channels;

using Microsoft.Extensions.Options;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Camera;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Producer side of the pipeline: opens the configured <see cref="IFrameSource"/>, writes every frame into the
/// bounded <see cref="Channel{Frame}"/> (capacity 2, DropOldest, dropped frames disposed) and keeps
/// <see cref="CameraStatus"/> up to date. If the source fails or ends it reconnects with exponential backoff
/// (1 s, 2 s, 4 s … max 10 s, reset after a successful frame). It never crashes the host.
/// Note: <c>Camera:Source</c> is read when a source is created, so switching it requires an app restart
/// (hot-swap is planned for M6).
/// </summary>
public sealed class CameraService(
    FrameSourceFactory sourceFactory,
    Channel<Frame> frames,
    CameraStatus status,
    IOptionsMonitor<CameraOptions> cameraOptions,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<CameraService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Optional Plan-B recording of raw frames; it lives across reconnects and is finalized on shutdown.
        using FrameRecorder? recorder = CreateRecorder();
        TimeSpan backoff = InitialBackoff;

        while (!stoppingToken.IsCancellationRequested)
        {
            IFrameSource? source = null;
            string sourceName = "-";
            try
            {
                source = sourceFactory.Create();
                sourceName = source.Name;
                status.ReportSourceOpening(sourceName);
                logger.LogInformation("Opening frame source {Source}", sourceName);

                bool receivedFrame = false;
                await foreach (Frame frame in source.ReadFramesAsync(stoppingToken))
                {
                    if (!receivedFrame)
                    {
                        receivedFrame = true;
                        backoff = InitialBackoff;
                        logger.LogInformation("Frame source {Source} connected", sourceName);
                    }

                    status.ReportFrame();

                    // The recorder only reads the frame during this call (never throws), so ownership is unaffected.
                    recorder?.TryWrite(frame);

                    // Ownership transfer: once written, the channel owns the frame. When the channel is full,
                    // DropOldest evicts the oldest frame and the itemDropped callback disposes it.
                    // TryWrite only fails if the channel was completed; then we still own the frame.
                    if (!frames.Writer.TryWrite(frame))
                    {
                        frame.Dispose();
                    }
                }

                logger.LogWarning("Frame source {Source} ended", sourceName);
                status.ReportDisconnected(FormattableString.Invariant($"Kamera-Quelle beendet – Neustart in {SecondsOf(backoff)} s"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Frame source {Source} failed", sourceName);
                // German text for the UI; the raw exception stays in the log.
                status.ReportDisconnected(FormattableString.Invariant($"Kamera nicht erreichbar – neuer Versuch in {SecondsOf(backoff)} s"));
            }
            finally
            {
                if (source is not null)
                {
                    await DisposeSourceAsync(source, sourceName);
                }
            }

            // Shutdown: OperationCanceledException from the delay propagates and stops the service cleanly.
            logger.LogInformation("Reconnecting frame source in {Delay}", backoff);
            await Task.Delay(backoff, timeProvider, stoppingToken);
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private FrameRecorder? CreateRecorder()
    {
        CameraOptions options = cameraOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.RecordTo))
        {
            return null;
        }

        try
        {
            string path = Path.GetFullPath(options.RecordTo, environment.ContentRootPath);
            return new FrameRecorder(path, options.RecordMaxSeconds, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invalid Camera:RecordTo {Path}; recording disabled", options.RecordTo);
            return null;
        }
    }

    private static int SecondsOf(TimeSpan delay) => (int)Math.Ceiling(delay.TotalSeconds);

    private async ValueTask DisposeSourceAsync(IFrameSource source, string sourceName)
    {
        try
        {
            await source.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Disposing frame source {Source} failed", sourceName);
        }
    }
}
