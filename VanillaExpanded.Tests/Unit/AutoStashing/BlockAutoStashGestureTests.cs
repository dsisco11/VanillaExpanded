using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Characterizes complete block stash gestures, timing, input gates, requests and presentation feedback.</summary>
[Collection("AutoStash")]
[Trait("Category", "Unit")]
public sealed class BlockAutoStashGestureTests
{
    #region Public API
    #region Start eligibility
    /// <summary>Starts each supported target through the real entry point with appropriate handling.</summary>
    [Theory]
    [InlineData("generic", EnumHandling.PreventDefault)]
    [InlineData("bloomery", EnumHandling.PreventDefault)]
    [InlineData("crate", EnumHandling.PreventSubsequent)]
    public void Start_EligibleTargetBeginsPreStash(string kind, EnumHandling expected)
    {
        using var test = new BlockGestureCase(kind);
        Assert.Equal(EStashingState.None, test.Behavior.State);
        Assert.Equal((true, expected), test.Start());
        Assert.Equal(EStashingState.PreStashGracePeriod, test.Behavior.State);
        test.AssertSubmission(0);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Never);
    }

    /// <summary>All crate modifier combinations preserve vanilla interaction unless both modifiers and an unrelated active slot apply.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Crate_ModifiersAndActiveItemPreserveVanillaException(bool ctrl, bool shift, bool activeMatching)
    {
        using var test = new BlockGestureCase("crate");
        test.Player.Object.Entity.Controls.CtrlKey = ctrl;
        test.Player.Object.Entity.Controls.ShiftKey = shift;
        if (activeMatching) test.Fixture.HotbarInventory[0].Itemstack = new ItemStack(test.Item, 1);
        else test.Fixture.HotbarInventory[0].Itemstack = new ItemStack(new VanillaExpanded.Tests.Mocks.MockItem(99, api: test.Fixture.Api)
            { Code = new AssetLocation("game:gesture-unrelated-active") }, 1);
        bool starts = ctrl && shift && !activeMatching;
        Assert.Equal((true, starts ? EnumHandling.PreventSubsequent : EnumHandling.PassThrough), test.Start());
        Assert.Equal(starts ? EStashingState.PreStashGracePeriod : EStashingState.None, test.Behavior.State);
        test.Step(test.Behavior.StashDelaySeconds);
        test.AssertSubmission(starts ? 1 : 0);
    }

    /// <summary>Disabled, server-side, and ineligible starts leave fresh state inactive without requests or feedback.</summary>
    [Theory]
    [InlineData("generic", "disabled", true)]
    [InlineData("generic", "server", true)]
    [InlineData("generic", "empty", false)]
    [InlineData("crate", "disabled", true)]
    [InlineData("crate", "server", true)]
    [InlineData("crate", "empty", false)]
    [InlineData("bloomery", "disabled", true)]
    [InlineData("bloomery", "server", true)]
    [InlineData("bloomery", "empty", false)]
    public void Start_RejectedGateDoesNotBeginGesture(string kind, string gate, bool result)
    {
        using var test = new BlockGestureCase(kind);
        if (gate == "disabled") VanillaExpandedModSystem.Config.EnableAutoStash = false;
        if (gate == "server") test.Fixture.WorldMock.SetupGet(world => world.Side).Returns(EnumAppSide.Server);
        if (gate == "empty") test.Fixture.BackpackInventory[0].Itemstack = null;
        Assert.Equal((result, EnumHandling.PassThrough), test.Start());
        Assert.Equal(EStashingState.None, test.Behavior.State);
        Assert.Equal((true, EnumHandling.PassThrough), test.Step(5));
        test.AssertSubmission(0);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Never);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(It.IsAny<VanillaExpanded.RadialProgress.IRadialProgressBar>()), Times.Never);
    }
    #endregion
    #region Timing and cancellation
    /// <summary>Exact configured delay submits once, subsequent steps do not duplicate, and the grace endpoint is inclusive.</summary>
    [Theory]
    [InlineData("generic", .5f)]
    [InlineData("generic", 1.25f)]
    [InlineData("crate", .5f)]
    [InlineData("crate", 1.25f)]
    [InlineData("bloomery", .5f)]
    [InlineData("bloomery", 1.25f)]
    public void Timing_ConfiguredDelayAndPostGraceProduceExactlyOneRequest(string kind, float delay)
    {
        using var test = new BlockGestureCase(kind);
        VanillaExpandedModSystem.Config.AutoStashDelay = delay;
        test.Start();
        Assert.Equal((true, EnumHandling.PreventSubsequent), test.Step(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds - .001f));
        Assert.Equal(EStashingState.PreStashGracePeriod, test.Behavior.State);
        Assert.Null(test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Never);
        Assert.Equal((true, EnumHandling.PreventSubsequent), test.Step(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds));
        Assert.Equal(EStashingState.Stashing, test.Behavior.State);
        Assert.Same(test.Progress.Object, test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Once);
        Assert.Equal(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds / delay, test.Progress.Object.Progress);
        Assert.Equal("AutoStashing", test.Progress.Object.Text);
        Assert.Equal((true, EnumHandling.PreventSubsequent), test.Step(delay - .001f));
        Assert.Equal((delay - .001f) / delay, test.Progress.Object.Progress);
        test.AssertSubmission(0);
        Assert.Equal((true, EnumHandling.PreventSubsequent), test.Step(delay));
        Assert.Equal(EStashingState.PostStashGracePeriod, test.Behavior.State);
        test.AssertSubmission(1);
        Assert.Equal(1, test.Progress.Object.Progress);
        Assert.Null(test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        // The current production order recreates progress before the post-stash state returns early.
        test.Step(delay + .001f);
        Assert.Same(test.Progress.Object, test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Exactly(2));
        Assert.Equal((true, EnumHandling.PreventSubsequent), test.Step(delay + BlockBehaviorAutoStashable.PostStashGracePeriodSeconds));
        Assert.Equal((false, EnumHandling.PreventSubsequent), test.Step(delay + BlockBehaviorAutoStashable.PostStashGracePeriodSeconds + .001f));
        test.AssertSubmission(1);
        Assert.Equal(EnumHandling.Handled, test.End(false));
        Assert.Equal(EStashingState.None, test.Behavior.State);
        Assert.Null(test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Exactly(2));
    }

    /// <summary>Cancel and stop before submission reset state; a restart uses a fresh timing sequence.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void End_BeforeSubmissionCanRestartWithoutStaleRequest(bool cancel)
    {
        using var test = new BlockGestureCase("generic");
        test.Start();
        test.Step(.2f);
        Assert.Same(test.Progress.Object, test.Behavior.CurrentProgress);
        EnumHandling ended = test.End(cancel);
        Assert.Equal(cancel ? EnumHandling.PassThrough : EnumHandling.Handled, ended);
        Assert.Equal(EStashingState.None, test.Behavior.State);
        Assert.Null(test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
        Assert.Equal((true, EnumHandling.PassThrough), test.Step(5));
        test.AssertSubmission(0);
        Assert.Equal((true, EnumHandling.PreventDefault), test.Start());
        test.Step(test.Behavior.StashDelaySeconds - .001f);
        test.AssertSubmission(0);
        test.Step(test.Behavior.StashDelaySeconds);
        test.AssertSubmission(1);
        Assert.Null(test.Behavior.CurrentProgress);
        test.ProgressProvider.Verify(provider => provider.CreateProgressBar(), Times.Exactly(2));
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Exactly(2));
    }

    /// <summary>Preserves the engine-visible one-argument behavior constructor alongside the optional presentation boundary.</summary>
    [Fact]
    public void Constructor_EngineCanCreateBehaviorWithOnlyBlock()
    {
        var constructor = typeof(BlockBehaviorAutoStashable).GetConstructor([typeof(Block)]);
        Assert.NotNull(constructor);
        Assert.IsType<BlockBehaviorAutoStashable>(constructor.Invoke([new Vintagestory.GameContent.BlockGenericTypedContainer()]));
    }
    #endregion
    #endregion
}

