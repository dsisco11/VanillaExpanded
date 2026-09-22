using Moq;

using VanillaExpanded.AutoStashing;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

[Trait("Category", "Unit")]
public class AutoStashInteractionStateTests
{
    private static readonly BlockSelection Selection = new()
    {
        Position = new BlockPos(0)
    };

    [Fact]
    public void OnBlockInteractStep_BeforeGracePeriod_RemainsPreStash()
    {
        // Arrange
        var behavior = new TestableAutoStashBehavior();
        behavior.SetState(EStashingState.PreStashGracePeriod);
        var world = CreateClientWorld();
        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        bool result = behavior.OnBlockInteractStep(
            BlockBehaviorAutoStashable.PreStashGracePeriodSeconds / 2,
            world,
            null!,
            Selection,
            ref handling);

        // Assert
        Assert.True(result);
        Assert.Equal(EStashingState.PreStashGracePeriod, behavior.State);
        Assert.Equal(EnumHandling.PreventSubsequent, handling);
    }

    [Fact]
    public void OnBlockInteractStep_AtGracePeriod_TransitionsToStashing()
    {
        // Arrange
        var behavior = new TestableAutoStashBehavior();
        behavior.SetState(EStashingState.PreStashGracePeriod);
        var world = CreateClientWorld();
        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        bool result = behavior.OnBlockInteractStep(
            BlockBehaviorAutoStashable.PreStashGracePeriodSeconds,
            world,
            null!,
            Selection,
            ref handling);

        // Assert
        Assert.True(result);
        Assert.Equal(EStashingState.Stashing, behavior.State);
    }

    [Fact]
    public void OnBlockInteractStep_PostStashState_DoesNotRequestAgain()
    {
        // Arrange
        var behavior = new TestableAutoStashBehavior();
        behavior.SetState(EStashingState.PostStashGracePeriod);
        var world = CreateClientWorld();
        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        bool result = behavior.OnBlockInteractStep(
            behavior.StashDelaySeconds,
            world,
            null!,
            Selection,
            ref handling);

        // Assert
        Assert.True(result);
        Assert.Equal(EStashingState.PostStashGracePeriod, behavior.State);
    }

    [Fact]
    public void OnBlockInteractCancel_ResetsState()
    {
        // Arrange
        var behavior = new TestableAutoStashBehavior();
        behavior.SetState(EStashingState.Stashing);
        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        behavior.OnBlockInteractCancel(
            0.2f,
            CreateClientWorld(),
            null!,
            Selection,
            ref handling);

        // Assert
        Assert.Equal(EStashingState.None, behavior.State);
    }

    [Fact]
    public void OnBlockInteractStop_ResetsStateAndHandlesInteraction()
    {
        // Arrange
        var behavior = new TestableAutoStashBehavior();
        behavior.SetState(EStashingState.Stashing);
        EnumHandling handling = EnumHandling.PassThrough;

        // Act
        behavior.OnBlockInteractStop(
            0.2f,
            CreateClientWorld(),
            null!,
            Selection,
            ref handling);

        // Assert
        Assert.Equal(EStashingState.None, behavior.State);
        Assert.Equal(EnumHandling.Handled, handling);
    }

    private static IWorldAccessor CreateClientWorld()
    {
        var world = new Mock<IWorldAccessor>();
        world.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        return world.Object;
    }

    private sealed class TestableAutoStashBehavior : BlockBehaviorAutoStashable
    {
        public TestableAutoStashBehavior()
            : base(null!)
        {
        }

        public EStashingState State => stashingState;

        public void SetState(EStashingState state)
        {
            stashingState = state;
        }
    }
}