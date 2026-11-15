using System.Threading.Channels;

using Microsoft.Extensions.Options;

using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Consumer side of the frame channel. M2 version: throttles to <c>Stream:MaxFps</c>, encodes the frame once
/// (with the Prüfzone drawn) and publishes the JPEG to <see cref="FrameBroadcaster"/> for <c>GET /stream</c>.
/// M3 inserts YOLO detection here (and M4 the RulesEngine + ZoneEntryTracker) before encoding.
/// Each frame is disposed by this service after processing.
/// </summary>
public sealed class DetectionService(
    Channel<Frame> frames,
    JpegFrameEncoder encoder,
    FrameBroadcaster broadcaster,
    IOptionsMonitor<StreamOptions> streamOptions,
    IOptionsMonitor<InspectionOptions> inspectionOptions,
    TimeProvider timeProvider,
    ILogger<DetectionService> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromMilliseconds(500);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessFramesAsync(stoppingToken);
        }
        finally
        {
            // Stop: no more writes, and dispose frames still queued so their pooled buffers are returned.
            frames.Writer.TryComplete();
            while (frames.Reader.TryRead(out Frame? pending))
            {
                pending.Dispose();
            }
        }
    }

    private async Task ProcessFramesAsync(CancellationToken stoppingToken)
    {
        await foreach (Frame frame in frames.Reader.ReadAllAsync(stoppingToken))
        {
            TimeSpan delay = TimeSpan.Zero;
            using (frame)
            {
                long started = timeProvider.GetTimestamp();
                try
                {
                    StreamOptions stream = streamOptions.CurrentValue;
                    InspectionOptions inspection = inspectionOptions.CurrentValue;

                    byte[] jpeg = encoder.Encode(frame, inspection.Zone, stream.JpegQuality, stream.MaxWidth);
                    broadcaster.Publish(jpeg);

                    // Throttle: wait out the rest of the frame interval. Meanwhile the channel keeps only the
                    // newest frames (DropOldest), so the next frame we read is still fresh.
                    TimeSpan minInterval = TimeSpan.FromSeconds(1.0 / stream.MaxFps);
                    delay = minInterval - timeProvider.GetElapsedTime(started);
                }
                catch (Exception ex)
                {
                    // One bad frame (or an invalid options reload) must not stop the pipeline.
                    long sequence = frame.Sequence;
                    logger.LogError(ex, "Processing frame {Sequence} failed", sequence);
                    delay = ErrorBackoff;
                }
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
        }
    }
}
