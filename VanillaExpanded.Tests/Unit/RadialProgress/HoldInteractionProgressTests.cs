using Moq;
using VanillaExpanded.RadialProgress;

namespace VanillaExpanded.Tests.Unit.RadialProgress;

[Trait("Category", "Unit")]
public sealed class HoldInteractionProgressTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateElapsed_ShowsAtExactGraceWithCallerLabelAndLayering(bool rendersTopmost)
    {
        var provider = new Mock<IProgressSystemProvider>(MockBehavior.Strict);
        var bar = new Mock<IRadialProgressBar>();
        bar.SetupAllProperties();
        provider.Setup(value => value.CreateProgressBar(rendersTopmost)).Returns(bar.Object);
        provider.Setup(value => value.RemoveProgressBar(bar.Object));
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "test-label");

        Assert.False(hold.UpdateElapsed(MathF.BitDecrement(0.1f), 0.5f, rendersTopmost));
        Assert.Null(hold.ProgressBar);
        provider.Verify(value => value.CreateProgressBar(It.IsAny<bool>()), Times.Never);

        Assert.False(hold.UpdateElapsed(0.1f, 0.5f, rendersTopmost));
        Assert.Equal(0.1f, hold.ElapsedSeconds);
        Assert.Equal(0.2f, bar.Object.Progress);
        Assert.Equal("test-label", bar.Object.Text);
        Assert.Same(bar.Object, hold.ProgressBar);

        hold.UpdateElapsed(0.25f, 0.5f, rendersTopmost);
        Assert.Equal(0.5f, bar.Object.Progress);
        provider.Verify(value => value.CreateProgressBar(rendersTopmost), Times.Once);
        hold.Reset();
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(1.25f)]
    public void UpdateElapsed_CompletesAtExactDelayOnceAndNeverRecreatesDisplay(float delay)
    {
        var provider = new Mock<IProgressSystemProvider>();
        var bar = new Mock<IRadialProgressBar>();
        bar.SetupAllProperties();
        provider.Setup(value => value.CreateProgressBar(true)).Returns(bar.Object);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.False(hold.UpdateElapsed(MathF.BitDecrement(delay), delay, true));
        Assert.False(hold.Completed);
        Assert.True(hold.UpdateElapsed(delay, delay, true));
        Assert.True(hold.Completed);
        Assert.Equal(1f, bar.Object.Progress);
        Assert.Null(hold.ProgressBar);

        Assert.False(hold.UpdateElapsed(delay + 1, delay, true));
        Assert.False(hold.Advance(1, delay, true));
        provider.Verify(value => value.CreateProgressBar(true), Times.Once);
        provider.Verify(value => value.RemoveProgressBar(bar.Object), Times.Once);
        hold.Reset();
        hold.Reset();
        provider.Verify(value => value.RemoveProgressBar(bar.Object), Times.Once);
    }

    [Fact]
    public void Advance_AccumulatesDeltasAndResetStartsFreshTiming()
    {
        var provider = new Mock<IProgressSystemProvider>();
        var bar = new Mock<IRadialProgressBar>();
        bar.SetupAllProperties();
        provider.Setup(value => value.CreateProgressBar(true)).Returns(bar.Object);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.False(hold.Advance(0.25f, 0.5f, true));
        Assert.Equal(0.25f, hold.ElapsedSeconds);
        hold.Reset();
        hold.Reset();
        Assert.Equal(0, hold.ElapsedSeconds);
        Assert.False(hold.Completed);
        Assert.Null(hold.ProgressBar);
        provider.Verify(value => value.RemoveProgressBar(bar.Object), Times.Once);

        Assert.False(hold.Advance(0.25f, 0.5f, true));
        Assert.True(hold.Advance(0.25f, 0.5f, true));
        hold.Reset();
        Assert.False(hold.Advance(0, 0.5f, true));
        Assert.False(hold.Completed);
        Assert.Equal(0, hold.ElapsedSeconds);
        provider.Verify(value => value.CreateProgressBar(true), Times.Exactly(2));
        provider.Verify(value => value.RemoveProgressBar(bar.Object), Times.Exactly(2));
    }

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.75f)]
    public void UpdateElapsed_CompletesWhenCallbackSkipsPastDelayAndClampsProgress(float elapsed)
    {
        var provider = new Mock<IProgressSystemProvider>();
        var bar = new Mock<IRadialProgressBar>();
        bar.SetupAllProperties();
        provider.Setup(value => value.CreateProgressBar(true)).Returns(bar.Object);
        var hold = new HoldInteractionProgress(provider.Object, 0, "hold");

        Assert.True(hold.UpdateElapsed(elapsed, 0.05f, true));
        Assert.Equal(1f, bar.Object.Progress);
        provider.Verify(value => value.RemoveProgressBar(bar.Object), Times.Once);
    }

    [Fact]
    public void Completion_BeforePresentationGraceDoesNotRequireDisplay()
    {
        var provider = new Mock<IProgressSystemProvider>(MockBehavior.Strict);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.True(hold.UpdateElapsed(0.05f, 0.05f, true));
        Assert.True(hold.Completed);
        Assert.Null(hold.ProgressBar);
        hold.Reset();
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public void MissingDisplay_DoesNotPreventCompletionOrCauseRemoval()
    {
        var provider = new Mock<IProgressSystemProvider>();
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.False(hold.Advance(0.25f, 0.5f, false));
        Assert.True(hold.Advance(0.25f, 0.5f, false));
        Assert.False(hold.Advance(1, 0.5f, false));
        Assert.True(hold.Completed);
        Assert.Null(hold.ProgressBar);
        hold.Reset();
        provider.Verify(value => value.RemoveProgressBar(It.IsAny<IRadialProgressBar>()), Times.Never);
    }

    [Fact]
    public void UnavailableDisplay_CanRecoverBeforeCompletion()
    {
        var provider = new Mock<IProgressSystemProvider>();
        var bar = new Mock<IRadialProgressBar>();
        bar.SetupAllProperties();
        provider.SetupSequence(value => value.CreateProgressBar(true)).Returns((IRadialProgressBar?)null).Returns(bar.Object);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.False(hold.UpdateElapsed(0.1f, 0.5f, true));
        Assert.Null(hold.ProgressBar);
        Assert.False(hold.UpdateElapsed(0.25f, 0.5f, true));
        Assert.Same(bar.Object, hold.ProgressBar);
        Assert.Equal(0.5f, bar.Object.Progress);
        hold.Reset();
        provider.Verify(value => value.RemoveProgressBar(bar.Object), Times.Once);
    }

    [Fact]
    public void Reset_ClearsOwnershipBeforeExternalCleanupThrows()
    {
        var provider = new Mock<IProgressSystemProvider>();
        var bar = Mock.Of<IRadialProgressBar>();
        var error = new InvalidOperationException("cleanup failed");
        provider.Setup(value => value.CreateProgressBar(true)).Returns(bar);
        provider.Setup(value => value.RemoveProgressBar(bar)).Throws(error);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");
        hold.Advance(0.25f, 0.5f, true);

        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => hold.Reset()));
        Assert.Null(hold.ProgressBar);
        hold.Reset();
        provider.Verify(value => value.RemoveProgressBar(bar), Times.Once);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidElapsedOrDelta_ThrowsWithoutCreatingDisplay(float invalid)
    {
        var provider = new Mock<IProgressSystemProvider>(MockBehavior.Strict);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.Throws<ArgumentOutOfRangeException>(() => hold.UpdateElapsed(invalid, 0.5f, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => hold.Advance(invalid, 0.5f, true));
        provider.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidDelay_ThrowsWithoutCreatingDisplay(float invalid)
    {
        var provider = new Mock<IProgressSystemProvider>(MockBehavior.Strict);
        var hold = new HoldInteractionProgress(provider.Object, 0.1f, "hold");

        Assert.Throws<ArgumentOutOfRangeException>(() => hold.UpdateElapsed(0.1f, invalid, true));
        provider.VerifyNoOtherCalls();
    }
}
