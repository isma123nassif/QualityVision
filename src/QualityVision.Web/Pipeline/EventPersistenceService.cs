using System.Threading.Channels;

using Microsoft.EntityFrameworkCore;

using QualityVision.Core.Inspection;
using QualityVision.Infrastructure.Persistence;

namespace QualityVision.Web.Pipeline;

/// <summary>
/// Persists every counted inspection to SQLite. <see cref="IInspectionLog.EventAdded"/> only writes into a bounded
/// channel (DropOldest, never blocks the detection thread); this service batches the inserts (≤ 50 events or 1 s).
/// </summary>
public sealed class EventPersistenceService : BackgroundService
{
    private const int MaxBatch = 50;
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);

    private readonly IInspectionLog _log;
    private readonly IDbContextFactory<InspectionDbContext> _dbFactory;
    private readonly ILogger<EventPersistenceService> _logger;
    private readonly Channel<InspectionEvent> _channel = Channel.CreateBounded<InspectionEvent>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    /// <summary>Creates the service.</summary>
    /// <param name="log">The inspection log whose events are persisted.</param>
    /// <param name="dbFactory">Factory for short-lived database contexts.</param>
    /// <param name="logger">Logger.</param>
    public EventPersistenceService(
        IInspectionLog log,
        IDbContextFactory<InspectionDbContext> dbFactory,
        ILogger<EventPersistenceService> logger)
    {
        _log = log;
        _dbFactory = dbFactory;
        _logger = logger;
        _log.EventAdded += OnEventAdded;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _log.EventAdded -= OnEventAdded;
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<InspectionEvent>(MaxBatch);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for the first event, then collect more for at most one second.
                batch.Add(await _channel.Reader.ReadAsync(stoppingToken).ConfigureAwait(false));
                using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                window.CancelAfter(MaxWait);
                try
                {
                    while (batch.Count < MaxBatch)
                    {
                        batch.Add(await _channel.Reader.ReadAsync(window.Token).ConfigureAwait(false));
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // One-second window elapsed: write what we have.
                }

                await WriteAsync(batch, CancellationToken.None).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown: flush below.
        }

        // Flush on shutdown: whatever was read into the batch plus what is still queued.
        _log.EventAdded -= OnEventAdded;
        while (_channel.Reader.TryRead(out InspectionEvent? pending))
        {
            batch.Add(pending);
        }

        await WriteAsync(batch, CancellationToken.None).ConfigureAwait(false);
    }

    private void OnEventAdded(InspectionEvent e) => _channel.Writer.TryWrite(e);

    private async Task WriteAsync(List<InspectionEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        try
        {
            await using InspectionDbContext db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            db.Inspections.AddRange(events.Select(static e => new InspectionRecord
            {
                Timestamp = e.Timestamp,
                ClassName = e.ClassName,
                DisplayName = e.DisplayName,
                Verdict = e.Verdict.ToString(),
                Confidence = e.Confidence,
            }));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Log and carry on: persistence problems must never stop the live pipeline.
            _logger.LogError(ex, "Persisting {Count} inspection events failed", events.Count);
        }
    }
}
