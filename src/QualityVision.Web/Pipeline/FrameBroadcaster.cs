namespace QualityVision.Web.Pipeline;

/// <summary>
/// Holds the latest annotated JPEG (encoded once by <see cref="DetectionService"/>) and lets any number of
/// <c>/stream</c> clients wait for the next one. Slow clients simply skip frames; they never slow the pipeline.
/// The published byte arrays are shared between readers and must be treated as immutable.
/// </summary>
public sealed class FrameBroadcaster
{
    private readonly Lock _gate = new();
    private (byte[] Jpeg, long Sequence)? _latest;
    private TaskCompletionSource<(byte[] Jpeg, long Sequence)> _next = NewWaiter();

    public (byte[] Jpeg, long Sequence)? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Stores the JPEG as the latest frame, increments the sequence and completes all waiters.</summary>
    public void Publish(byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(jpeg);

        TaskCompletionSource<(byte[] Jpeg, long Sequence)> waiters;
        (byte[] Jpeg, long Sequence) published;
        lock (_gate)
        {
            published = (jpeg, (_latest?.Sequence ?? 0) + 1);
            _latest = published;
            waiters = _next;
            _next = NewWaiter();
        }

        // Completed outside the lock; continuations run asynchronously, so Publish never runs client code.
        waiters.TrySetResult(published);
    }

    /// <summary>
    /// Returns immediately if <see cref="Latest"/> is newer than <paramref name="afterSequence"/>;
    /// otherwise completes on the next <see cref="Publish"/>. Cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public Task<(byte[] Jpeg, long Sequence)> WaitForNextAsync(long afterSequence, CancellationToken ct)
    {
        Task<(byte[] Jpeg, long Sequence)> next;
        lock (_gate)
        {
            if (_latest is { } latest && latest.Sequence > afterSequence)
            {
                return Task.FromResult(latest);
            }

            next = _next.Task;
        }

        return next.WaitAsync(ct);
    }

    private static TaskCompletionSource<(byte[] Jpeg, long Sequence)> NewWaiter() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
