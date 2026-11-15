using QualityVision.Web.Pipeline;

namespace QualityVision.Tests.Web;

public sealed class FrameUploadGateTests
{
    [Fact]
    public void TryEnter_TwoAcquisitions_BothSucceed()
    {
        var sut = new FrameUploadGate();

        Assert.True(sut.TryEnter());
        Assert.True(sut.TryEnter());
    }

    [Fact]
    public void TryEnter_ThirdAcquisition_FailsImmediately()
    {
        var sut = new FrameUploadGate();
        sut.TryEnter();
        sut.TryEnter();

        Assert.False(sut.TryEnter());
    }

    [Fact]
    public void TryEnter_AfterRelease_SlotIsFreeAgain()
    {
        var sut = new FrameUploadGate();
        sut.TryEnter();
        sut.TryEnter();
        Assert.False(sut.TryEnter());

        sut.Release();

        Assert.True(sut.TryEnter());
        Assert.False(sut.TryEnter());
    }
}
