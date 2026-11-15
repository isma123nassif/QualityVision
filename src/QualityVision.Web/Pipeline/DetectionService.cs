using System.Threading.Channels;

using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Core.Frames;
using QualityVision.Infrastructure.Detection;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Consumer side of the frame channel. Per frame: YOLO detection (timed), metrics update, one annotated JPEG
/// (Prüfzone + boxes drawn) published to <see cref="FrameBroadcaster"/> for <c>GET /stream</c>.
/// M4 inserts the RulesEngine + ZoneEntryTracker after detection. <c>Stream:MaxFps</c> stays an upper bound.
/// Each frame is disposed by this service after processing.
/// </summary>
public sealed class DetectionService(
    Channel<Frame> frames,
    IObjectDetector detector,
    YoloObjectDetector warmUpTarget,
    SkiaFrameRenderer renderer,
    PipelineMetrics metrics,
    FrameBroadcaster broadcaster,
    IOptionsMonitor<StreamOptions> streamOptions,
    IOptionsMonitor<InspectionOptions> inspectionOptions,
    TimeProvider timeProvider,
    ILogger<DetectionService> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(5);

    private long _lastErrorLog;
    private bool _errorLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Return control to the host immediately: warm-up and inference are CPU-bound and must not block startup.
        await Task.Yield();

        try
        {
            try
            {
                long started = timeProvider.GetTimestamp();
                warmUpTarget.WarmUp();
                metrics.MarkModelReady();
                logger.LogInformation("Model warm-up took {Ms} ms", timeProvider.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Warm-up is an optimisation: continue into the frame loop; health stays Degraded until a success.
                logger.LogError(ex, "Model warm-up failed; continuing without warm-up");
            }

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

                    metrics.MarkFrameSeen();
                    IReadOnlyList<Detection> detections;
                    try
                    {
                        long inferenceStart = timeProvider.GetTimestamp();
                        detections = detector.Detect(frame);
                        double inferenceMs = timeProvider.GetElapsedTime(inferenceStart).TotalMilliseconds;
                        metrics.Record(detections, inferenceMs);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Inference failure must not freeze the live stream: show the raw frame without boxes.
                        LogFrameError(ex, frame.Sequence);
                        detections = [];
                    }

                    byte[] jpeg = renderer.RenderJpeg(frame, detections, inspection.Zone, stream.JpegQuality, stream.MaxWidth);
                    broadcaster.Publish(jpeg);

                    // Upper bound: wait out the rest of the frame interval. Meanwhile the channel keeps only the
                    // newest frames (DropOldest), so the next frame we read is still fresh.
                    TimeSpan minInterval = TimeSpan.FromSeconds(1.0 / stream.MaxFps);
                    delay = minInterval - timeProvider.GetElapsedTime(started);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad frame (or an invalid options reload) must not stop the pipeline.
                    LogFrameError(ex, frame.Sequence);
                    delay = ErrorBackoff;
                }
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, stoppingToken);
            }
        }
    }

    /// <summary>Logs at most once per 5 s so a persistently failing frame cannot flood the log.</summary>
    private void LogFrameError(Exception ex, long sequence)
    {
        if (_errorLogged && timeProvider.GetElapsedTime(_lastErrorLog) < ErrorLogInterval)
        {
            return;
        }

        _errorLogged = true;
        _lastErrorLog = timeProvider.GetTimestamp();
        logger.LogError(ex, "Processing frame {Sequence} failed", sequence);
    }
}
