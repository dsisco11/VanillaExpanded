using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for timing constants and transfer-related configuration in BlockBehaviorAutoStashable.
/// Note: Full transfer logic tests require more complex mocking infrastructure due to
/// protobuf dependencies in the game's BlockAccessor. These tests focus on constants
/// and configuration that can be tested without full game environment mocking.
/// </summary>
[Trait("Category", "Unit")]
public class AutoStashTransferTests
{
    #region Timing Constants Tests

    [Fact]
    public void StashDelaySeconds_HasExpectedValue()
    {
        // Assert
        Assert.Equal(0.5f, BlockBehaviorAutoStashable.StashDelaySeconds);
    }

    [Fact]
    public void PreStashGracePeriodSeconds_HasExpectedValue()
    {
        // Assert
        Assert.Equal(0.1f, BlockBehaviorAutoStashable.PreStashGracePeriodSeconds);
    }

    [Fact]
    public void PostStashGracePeriodSeconds_HasExpectedValue()
    {
        // Assert
        Assert.Equal(0.4f, BlockBehaviorAutoStashable.PostStashGracePeriodSeconds);
    }

    [Fact]
    public void TotalStashDuration_SumOfDelayAndGracePeriods()
    {
        // Arrange
        var expectedTotal = BlockBehaviorAutoStashable.StashDelaySeconds + BlockBehaviorAutoStashable.PostStashGracePeriodSeconds;

        // Assert - Total interaction time is stashDelay + postStashGracePeriod
        Assert.Equal(0.9f, expectedTotal);
    }

    [Fact]
    public void PreStashGracePeriod_IsLessThanStashDelay()
    {
        // The pre-stash grace period should always be less than the stash delay
        // to ensure the UI shows before stashing occurs
        Assert.True(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds < BlockBehaviorAutoStashable.StashDelaySeconds);
    }

    [Fact]
    public void TimingConstants_ArePositive()
    {
        // All timing constants should be positive values
        Assert.True(BlockBehaviorAutoStashable.StashDelaySeconds > 0);
        Assert.True(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds > 0);
        Assert.True(BlockBehaviorAutoStashable.PostStashGracePeriodSeconds > 0);
    }

    #endregion
}
