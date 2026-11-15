using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class FrameBroadcasterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Latest_NothingPublished_IsNull()
    {
        Assert.Null(new FrameBroadcaster().Latest);
    }

    [Fact]
    public void Publish_Twice_IncrementsSequenceAndKeepsLatest()
    {
        var sut = new FrameBroadcaster();
        var a = new byte[] { 1 };
        var b = new byte[] { 2 };

        sut.Publish(a);
        Assert.Equal(1, sut.Latest!.Value.Sequence);
        sut.Publish(b);

        Assert.Equal(2, sut.Latest!.Value.Sequence);
        Assert.Same(b, sut.Latest!.Value.Jpeg);
    }

    [Fact]
    public void Publish_NullJpeg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FrameBroadcaster().Publish(null!));
    }

    [Fact]
    public async Task WaitForNextAsync_LatestIsNewer_ReturnsImmediately()
    {
        var sut = new FrameBroadcaster();
        var jpeg = new byte[] { 9 };
        sut.Publish(jpeg);

        var task = sut.WaitForNextAsync(0, CancellationToken.None);

        Assert.True(task.IsCompleted);
        var result = await task;
        Assert.Equal(1, result.Sequence);
        Assert.Same(jpeg, result.Jpeg);
    }

    [Fact]
    public async Task WaitForNextAsync_NothingPublished_CompletesOnPublish()
    {
        var sut = new FrameBroadcaster();

        var task = sut.WaitForNextAsync(0, CancellationToken.None);
        Assert.False(task.IsCompleted);
        var jpeg = new byte[] { 7 };
        sut.Publish(jpeg);

        var result = await task.WaitAsync(Timeout);
        Assert.Equal(1, result.Sequence);
        Assert.Same(jpeg, result.Jpeg);
    }

    [Fact]
    public async Task WaitForNextAsync_AlreadyAtLatest_WaitsForNextPublish()
    {
        var sut = new FrameBroadcaster();
        sut.Publish(new byte[] { 1 });

        var task = sut.WaitForNextAsync(1, CancellationToken.None);
        Assert.False(task.IsCompleted);
        sut.Publish(new byte[] { 2 });

        var result = await task.WaitAsync(Timeout);
        Assert.Equal(2, result.Sequence);
    }

    [Fact]
    public async Task Publish_MultipleWaiters_CompletesAll()
    {
        var sut = new FrameBroadcaster();
        var waiters = Enumerable.Range(0, 5).Select(_ => sut.WaitForNextAsync(0, CancellationToken.None)).ToArray();

        sut.Publish(new byte[] { 1 });

        var results = await Task.WhenAll(waiters).WaitAsync(Timeout);
        Assert.All(results, r => Assert.Equal(1, r.Sequence));
    }

    [Fact]
    public async Task WaitForNextAsync_Cancelled_ThrowsOperationCanceled()
    {
        var sut = new FrameBroadcaster();
        using var cts = new CancellationTokenSource();

        var task = sut.WaitForNextAsync(0, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Timeout));
    }

    [Fact]
    public async Task WaitForNextAsync_CancelledWaiter_DoesNotBreakOtherWaiters()
    {
        var sut = new FrameBroadcaster();
        using var cts = new CancellationTokenSource();
        var cancelled = sut.WaitForNextAsync(0, cts.Token);
        var other = sut.WaitForNextAsync(0, CancellationToken.None);

        cts.Cancel();
        sut.Publish(new byte[] { 1 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Timeout));
        Assert.Equal(1, (await other.WaitAsync(Timeout)).Sequence);
    }

    [Fact]
    public async Task WaitForNextAsync_OneWaiterCancelled_OtherStaysPendingUntilPublish()
    {
        var sut = new FrameBroadcaster();
        using var cts = new CancellationTokenSource();
        var cancelled = sut.WaitForNextAsync(0, cts.Token);
        var other = sut.WaitForNextAsync(0, CancellationToken.None);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Timeout));

        Assert.False(other.IsCompleted);
        sut.Publish(new byte[] { 1 });
        Assert.Equal(1, (await other.WaitAsync(Timeout)).Sequence);
    }

    [Fact]
    public async Task WaitForNextAsync_WaitersWithOwnTokens_CancellingOneLeavesOtherUncancelled()
    {
        var sut = new FrameBroadcaster();
        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();
        var a = sut.WaitForNextAsync(0, ctsA.Token);
        var b = sut.WaitForNextAsync(0, ctsB.Token);

        ctsA.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(Timeout));

        Assert.False(b.IsCompleted);
        sut.Publish(new byte[] { 5 });
        Assert.Equal(1, (await b.WaitAsync(Timeout)).Sequence);
    }

    [Fact]
    public async Task WaitForNextAsync_PreCancelledToken_ThrowsEvenWhenNothingPublished()
    {
        var sut = new FrameBroadcaster();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.WaitForNextAsync(0, cts.Token).WaitAsync(Timeout));
    }
}
