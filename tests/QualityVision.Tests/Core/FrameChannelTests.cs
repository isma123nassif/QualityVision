using System.Threading.Channels;

using QualityVision.Core.Frames;

namespace QualityVision.Tests.Core;

/// <summary>Verifies the pipeline's Channel&lt;Frame&gt; configuration disposes dropped frames.</summary>
public sealed class FrameChannelTests
{
    private static Channel<Frame> CreatePipelineChannel() =>
        Channel.CreateBounded<Frame>(
            new BoundedChannelOptions(2)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            f => f.Dispose());

    private static Frame NewFrame(long seq, List<long> returnedSequences) =>
        new(new byte[16], 2, 2, seq, DateTimeOffset.UnixEpoch, "t", _ => returnedSequences.Add(seq));

    [Fact]
    public void TryWrite_ChannelFull_DisposesOldestFrame()
    {
        var returned = new List<long>();
        var channel = CreatePipelineChannel();

        for (long i = 1; i <= 3; i++)
        {
            Assert.True(channel.Writer.TryWrite(NewFrame(i, returned)));
        }

        Assert.Equal(new long[] { 1 }, returned);
    }

    [Fact]
    public void TryWrite_ManyFrames_OnlyLastTwoRemainAndOthersDisposed()
    {
        var returned = new List<long>();
        var channel = CreatePipelineChannel();

        for (long i = 1; i <= 5; i++)
        {
            channel.Writer.TryWrite(NewFrame(i, returned));
        }

        Assert.Equal(new long[] { 1, 2, 3 }, returned);
        Assert.True(channel.Reader.TryRead(out var a));
        Assert.True(channel.Reader.TryRead(out var b));
        Assert.False(channel.Reader.TryRead(out _));
        Assert.Equal(4, a.Sequence);
        Assert.Equal(5, b.Sequence);
        Assert.Equal(3, returned.Count); // reading must not dispose
        a.Dispose();
        b.Dispose();
    }

    [Fact]
    public void TryWrite_ChannelNotFull_DisposesNothing()
    {
        var returned = new List<long>();
        var channel = CreatePipelineChannel();

        channel.Writer.TryWrite(NewFrame(1, returned));
        channel.Writer.TryWrite(NewFrame(2, returned));

        Assert.Empty(returned);
    }
}
