namespace QualityVision.Web.Pipeline;

/// <summary>
/// Caps the number of phone frame uploads that are read and decoded concurrently, so a misbehaving client
/// cannot pile up decode work. Acquisition never waits: callers answer 503 when no slot is free.
/// </summary>
public sealed class FrameUploadGate
{
    private const int MaxConcurrentUploads = 2;

    private readonly SemaphoreSlim _semaphore = new(MaxConcurrentUploads, MaxConcurrentUploads);

    /// <summary>Tries to take a slot without waiting. A <c>true</c> result must be paired with <see cref="Release"/>.</summary>
    public bool TryEnter() => _semaphore.Wait(0);

    public void Release() => _semaphore.Release();
}
