using Moq;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>
/// Tests for AutoStashing inventory transfer behavior.
/// Covers scenarios for stashing items from player inventory to containers/crates.
/// </summary>
[Trait("Category", "Unit")]
public class AutoStashTransferTests
{
    #region Test Infrastructure

    /// <summary>
    /// Creates a VsTestFixture configured for server-side AutoStashing tests.
    /// </summary>
    private static VsTestFixture CreateFixture(
        MockItem[]? backpackItems = null,
        MockItem[]? hotbarItems = null)
    {
        var fixture = VsTestFixture.Server();

        if (backpackItems is not null)
        {
            fixture.WithBackpackItems(backpackItems);
        }

        if (hotbarItems is not null)
        {
            // AutoStash tests use hotbar slots starting at 0 (unlike EquipLightSource which uses slot 0 as active)
            for (int i = 0; i < hotbarItems.Length && i < fixture.HotbarInventory.Count; i++)
            {
                fixture.WithHotbarSlot(i, hotbarItems[i]);
            }
        }

        return fixture;
    }

    #endregion

    #region Timing Constants Tests

    [Fact]
    public void StashDelaySeconds_HasExpectedDefaultValue()
    {
        // Arrange - Create instance to access the instance field
        var behavior = new BlockBehaviorAutoStashable(null!);

        // Assert - Default value when config is null
        Assert.Equal(0.5f, behavior.StashDelaySeconds);
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
        var behavior = new BlockBehaviorAutoStashable(null!);
        var expectedTotal = behavior.StashDelaySeconds + BlockBehaviorAutoStashable.PostStashGracePeriodSeconds;

        // Assert - Total interaction time is stashDelay + postStashGracePeriod
        Assert.Equal(0.9f, expectedTotal);
    }

    [Fact]
    public void PreStashGracePeriod_IsLessThanStashDelay()
    {
        // Arrange
        var behavior = new BlockBehaviorAutoStashable(null!);

        // The pre-stash grace period should always be less than the stash delay
        // to ensure the UI shows before stashing occurs
        Assert.True(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds < behavior.StashDelaySeconds);
    }

    [Fact]
    public void TimingConstants_ArePositive()
    {
        // Arrange
        var behavior = new BlockBehaviorAutoStashable(null!);

        // All timing constants should be positive values
        Assert.True(behavior.StashDelaySeconds > 0);
        Assert.True(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds > 0);
        Assert.True(BlockBehaviorAutoStashable.PostStashGracePeriodSeconds > 0);
    }

    #endregion

    #region AutoStashToGenericContainer - Empty/No Match Tests

    [Fact]
    public void AutoStashToGenericContainer_EmptyContainer_ReturnsFalse()
    {
        // Arrange - Container is empty, player has items
        var fixture = CreateFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var container = MockBlockEntityContainer.Empty();

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return false (empty container has no item types to match)
        Assert.False(result);
    }

    [Fact]
    public void AutoStashToGenericContainer_NoMatchingItems_ReturnsFalse()
    {
        // Arrange - Container has itemA, player has itemB (different types)
        var containerItem = MockItem.CreateNonLightSource(id: 1);
        containerItem.Code = new AssetLocation("game", "item-a");

        var playerItem = MockItem.CreateNonLightSource(id: 2);
        playerItem.Code = new AssetLocation("game", "item-b");

        var fixture = CreateFixture(
            backpackItems: [playerItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return false (no matching item types)
        Assert.False(result);
    }

    [Fact]
    public void AutoStashToGenericContainer_PlayerInventoryEmpty_ReturnsFalse()
    {
        // Arrange - Container has items, player inventories are empty
        var containerItem = MockItem.CreateNonLightSource(id: 1);
        containerItem.Code = new AssetLocation("game", "test-item");

        var fixture = CreateFixture(); // Empty player inventories

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return false (player has no items to stash)
        Assert.False(result);
    }

    #endregion

    #region AutoStashToGenericContainer - Successful Stash Tests

    [Fact]
    public void AutoStashToGenericContainer_MatchingItemsInBackpack_StashesToContainer()
    {
        // Arrange - Container and player both have same item type
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var fixture = CreateFixture(
            backpackItems: [sharedItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);
        int initialContainerCount = container.GetNonEmptyStacks().Length;

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return true and stash items
        Assert.True(result);
        // Player's backpack should be empty after stash
        Assert.True(fixture.BackpackInventory[0].Empty);
    }

    [Fact]
    public void AutoStashToGenericContainer_MatchingItemsInHotbar_StashesToContainer()
    {
        // Arrange - Container has item, player has matching item in hotbar
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var fixture = CreateFixture(
            hotbarItems: [sharedItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.HotbarInventory[0].Empty);
    }

    [Fact]
    public void AutoStashToGenericContainer_ItemsInBothInventories_StashesBoth()
    {
        // Arrange - Player has matching items in both backpack and hotbar
        var sharedItem1 = MockItem.CreateNonLightSource(id: 1);
        sharedItem1.Code = new AssetLocation("game", "shared-item");

        var sharedItem2 = MockItem.CreateNonLightSource(id: 2);
        sharedItem2.Code = new AssetLocation("game", "shared-item"); // Same code

        var fixture = CreateFixture(
            backpackItems: [sharedItem1],
            hotbarItems: [sharedItem2]);

        var containerItem = MockItem.CreateNonLightSource(id: 3);
        containerItem.Code = new AssetLocation("game", "shared-item");

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Both inventories should be emptied
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
        Assert.True(fixture.HotbarInventory[0].Empty);
    }

    [Fact]
    public void AutoStashToGenericContainer_MultipleMatchingTypes_StashesAll()
    {
        // Arrange - Container has multiple item types, player has all of them
        var itemA = MockItem.CreateNonLightSource(id: 1);
        itemA.Code = new AssetLocation("game", "item-a");

        var itemB = MockItem.CreateNonLightSource(id: 2);
        itemB.Code = new AssetLocation("game", "item-b");

        var playerItemA = MockItem.CreateNonLightSource(id: 3);
        playerItemA.Code = new AssetLocation("game", "item-a");

        var playerItemB = MockItem.CreateNonLightSource(id: 4);
        playerItemB.Code = new AssetLocation("game", "item-b");

        var fixture = CreateFixture(
            backpackItems: [playerItemA, playerItemB]);

        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, itemA }, { 1, itemB } }, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
        Assert.True(fixture.BackpackInventory[1].Empty);
    }

    [Fact]
    public void AutoStashToGenericContainer_OnlyMatchingTypesStashed_NonMatchingRemains()
    {
        // Arrange - Player has both matching and non-matching items
        var matchingItem = MockItem.CreateNonLightSource(id: 1);
        matchingItem.Code = new AssetLocation("game", "matching-item");

        var nonMatchingItem = MockItem.CreateNonLightSource(id: 2);
        nonMatchingItem.Code = new AssetLocation("game", "non-matching-item");

        var fixture = CreateFixture(
            backpackItems: [matchingItem, nonMatchingItem]);

        var containerItem = MockItem.CreateNonLightSource(id: 3);
        containerItem.Code = new AssetLocation("game", "matching-item");

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Only matching item stashed, non-matching remains
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty); // Matching item stashed
        Assert.False(fixture.BackpackInventory[1].Empty); // Non-matching item remains
        Assert.Equal("non-matching-item", fixture.BackpackInventory[1].Itemstack.Collectible.Code.Path);
    }

    #endregion

    #region AutoStashToCrate - Empty/No Match Tests

    [Fact]
    public void AutoStashToCrate_EmptyCrate_ReturnsFalse()
    {
        // Arrange - Crate is empty, player has items
        var fixture = CreateFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var crate = MockBlockEntityCrate.Empty(api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert - Should return false (empty crate has no accepted item type)
        Assert.False(result);
    }

    [Fact]
    public void AutoStashToCrate_PlayerHasDifferentItems_ReturnsFalse()
    {
        // Arrange - Crate has itemA, player has itemB
        var crateItem = MockItem.CreateNonLightSource(id: 1);
        crateItem.Code = new AssetLocation("game", "crate-item");

        var playerItem = MockItem.CreateNonLightSource(id: 2);
        playerItem.Code = new AssetLocation("game", "different-item");

        var fixture = CreateFixture(
            backpackItems: [playerItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(crateItem, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region AutoStashToCrate - Successful Stash Tests

    [Fact]
    public void AutoStashToCrate_MatchingItems_StashesToCrate()
    {
        // Arrange - Crate and player have same item type
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var playerItem = MockItem.CreateNonLightSource(id: 2);
        playerItem.Code = new AssetLocation("game", "shared-item"); // Same code

        var fixture = CreateFixture(
            backpackItems: [playerItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(sharedItem, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
    }

    [Fact]
    public void AutoStashToCrate_MultipleItemTypes_OnlyMatchingTypeStashed()
    {
        // Arrange - Player has multiple item types, crate only accepts one
        var crateItem = MockItem.CreateNonLightSource(id: 1);
        crateItem.Code = new AssetLocation("game", "crate-accepted");

        var matchingItem = MockItem.CreateNonLightSource(id: 2);
        matchingItem.Code = new AssetLocation("game", "crate-accepted");

        var nonMatchingItem = MockItem.CreateNonLightSource(id: 3);
        nonMatchingItem.Code = new AssetLocation("game", "not-accepted");

        var fixture = CreateFixture(
            backpackItems: [matchingItem, nonMatchingItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(crateItem, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert - Only matching item stashed
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty); // Matching stashed
        Assert.False(fixture.BackpackInventory[1].Empty); // Non-matching remains
    }

    #endregion

    #region GetStashableItems Tests

    [Fact]
    public void GetStashableItems_NullContainer_ReturnsEmptySet()
    {
        // Arrange
        var fixture = CreateFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player,
            null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_EmptyContainer_ReturnsEmptySet()
    {
        // Arrange
        var fixture = CreateFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var container = MockBlockEntityContainer.Empty(api: fixture.Api);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player,
            container.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_MatchingItems_ReturnsIntersection()
    {
        // Arrange - Player and container share item with ID 1
        var playerItem = MockItem.CreateNonLightSource(id: 1);
        var containerItem = MockItem.CreateNonLightSource(id: 1); // Same ID

        var fixture = CreateFixture(
            backpackItems: [playerItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player,
            container.Object);

        // Assert - Should contain the shared item ID
        Assert.Single(result);
        Assert.Contains(1, result);
    }

    [Fact]
    public void GetStashableItems_NoOverlap_ReturnsEmptySet()
    {
        // Arrange - Player has ID 1, container has ID 2
        var playerItem = MockItem.CreateNonLightSource(id: 1);
        var containerItem = MockItem.CreateNonLightSource(id: 2);

        var fixture = CreateFixture(
            backpackItems: [playerItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player,
            container.Object);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_ItemsInHotbar_IncludedInResult()
    {
        // Arrange - Matching item is in hotbar, not backpack
        var hotbarItem = MockItem.CreateNonLightSource(id: 5);
        var containerItem = MockItem.CreateNonLightSource(id: 5);

        var fixture = CreateFixture(
            hotbarItems: [hotbarItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player,
            container.Object);

        // Assert
        Assert.Single(result);
        Assert.Contains(5, result);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void AutoStashToGenericContainer_NoExceptionOnEmptyInventories()
    {
        // Arrange - All inventories empty
        var fixture = CreateFixture();
        var container = MockBlockEntityContainer.Empty(api: fixture.Api);

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            BlockBehaviorAutoStashable.AutoStashToGenericContainer(
                fixture.World,
                fixture.Player,
                container.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void AutoStashToCrate_NoExceptionOnEmptyInventories()
    {
        // Arrange
        var fixture = CreateFixture();
        var crate = MockBlockEntityCrate.Empty(api: fixture.Api);

        // Act & Assert
        var exception = Record.Exception(() =>
            BlockBehaviorAutoStashable.AutoStashToCrate(
                fixture.World,
                fixture.Player,
                crate.Object));

        Assert.Null(exception);
    }

    #endregion
}
