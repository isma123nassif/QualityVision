using System.Threading.Channels;

using Microsoft.Extensions.Options;

using QualityVision.Core.Detection;
using QualityVision.Core.Frames;
using QualityVision.Core.Inspection;
using QualityVision.Core.Rules;
using QualityVision.Infrastructure.Detection;
using QualityVision.Infrastructure.Imaging;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Consumer side of the frame channel. Per frame: YOLO detection (timed), verdicts from the RulesEngine (one
/// <see cref="RuleSet"/> snapshot per frame), Prüfzone filter + <see cref="ZoneEntryTracker"/> (new entries go to
/// <see cref="IInspectionLog"/>), metrics update, and one annotated JPEG published to <see cref="FrameBroadcaster"/>
/// for <c>GET /stream</c>. <c>Stream:MaxFps</c> stays an upper bound. Each frame is disposed after processing.
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
    IRuleSetProvider ruleSetProvider,
    IInspectionLog inspectionLog,
    TimeProvider timeProvider,
    ILogger<DetectionService> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromSeconds(5);

    private long _lastErrorLog;
    private bool _errorLogged;
    // Plain field: only the single detection loop touches it (no Interlocked needed).
    private long _eventSequence;
    private ZoneEntryTracker? _tracker;

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
                    IReadOnlyList<ClassifiedDetection> classified;
                    bool detectionFailed = false;
                    try
                    {
                        long inferenceStart = timeProvider.GetTimestamp();
                        IReadOnlyList<Detection> detections = detector.Detect(frame);
                        double inferenceMs = timeProvider.GetElapsedTime(inferenceStart).TotalMilliseconds;

                        // One rule snapshot per frame: every detection of this frame is judged by the same rules.
                        RuleSet rules = ruleSetProvider.Current;
                        classified = detections
                            .Select(d => new ClassifiedDetection(d, RulesEngine.Evaluate(d, rules)))
                            .ToArray();
                        metrics.Record(classified, inferenceMs);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Inference failure must not freeze the live stream: show the raw frame (and the zone)
                        // without boxes, and do not feed the tracker, so a failed frame never counts as absence.
                        LogFrameError(ex, frame.Sequence);
                        classified = [];
                        detectionFailed = true;
                    }

                    if (!detectionFailed)
                    {
                        CountZoneEntries(classified, inspection);
                    }

                    byte[] jpeg = renderer.RenderJpeg(frame, classified, inspection.Zone, stream.JpegQuality, stream.MaxWidth);
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

    /// <summary>
    /// Prüfzone + debounce: feeds the in-zone detections to the tracker and records every new entry as an
    /// <see cref="InspectionEvent"/> (time stamped here, not in the tracker).
    /// </summary>
    private void CountZoneEntries(IReadOnlyList<ClassifiedDetection> classified, InspectionOptions inspection)
    {
        // The tracker is single-threaded (only this loop uses it); recreated if AbsenceFrames is changed on reload.
        IReadOnlyList<ClassifiedDetection> inZone = InspectionZone.FilterInside(classified, inspection.Zone);
        if (_tracker is null || _tracker.AbsenceFrames != inspection.AbsenceFrames)
        {
            bool recreated = _tracker is not null;
            _tracker = new ZoneEntryTracker(inspection.AbsenceFrames);
            if (recreated)
            {
                // Prime the new tracker with this frame and discard the entries: otherwise everything already in
                // the zone would be counted a second time just because AbsenceFrames changed.
                _ = _tracker.Update(inZone);
                return;
            }
        }

        IReadOnlyList<ClassifiedDetection> entries = _tracker.Update(inZone);
        foreach (ClassifiedDetection entry in entries)
        {
            var inspectionEvent = new InspectionEvent(
                ++_eventSequence,
                timeProvider.GetUtcNow(),
                entry.Detection.ClassName,
                GermanLabels.Get(entry.Detection.ClassName),
                entry.Verdict,
                entry.Detection.Confidence);
            inspectionLog.Add(inspectionEvent);
            logger.LogInformation("Inspection {Sequence}: {Class} → {Verdict} ({Confidence:P0})",
                inspectionEvent.Sequence, inspectionEvent.ClassName, inspectionEvent.Verdict, inspectionEvent.Confidence);
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
