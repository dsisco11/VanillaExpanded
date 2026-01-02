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
    /// Test fixture that creates real InventoryGeneric instances for player inventories
    /// and mock container for stashing operations.
    /// </summary>
    private class TestFixture
    {
        public Mock<IWorldAccessor> WorldMock { get; }
        public IWorldAccessor World => WorldMock.Object;
        public Mock<ICoreAPI> ApiMock { get; private set; } = null!;
        public ICoreAPI Api => ApiMock.Object;
        public MockPlayer Player { get; }

        public InventoryGeneric BackpackInventory { get; }
        public InventoryGeneric HotbarInventory { get; }

        private readonly Mock<IInventoryNetworkUtil> _invNetworkUtilMock;

        public TestFixture(
            MockItem[]? backpackItems = null,
            MockItem[]? hotbarItems = null,
            int backpackSize = 10,
            int hotbarSize = 10)
        {
            // Create simple mock world accessor (avoids MockBlockAccessor protobuf issues)
            WorldMock = new Mock<IWorldAccessor>();
            WorldMock.Setup(w => w.Side).Returns(EnumAppSide.Server);
            WorldMock.Setup(w => w.Logger).Returns(new Mock<ILogger>().Object);

            // Create real inventories with null! API, then configure
            BackpackInventory = new InventoryGeneric(backpackSize, GlobalConstants.backpackInvClassName, "backpack-1", null!);
            HotbarInventory = new InventoryGeneric(hotbarSize, GlobalConstants.hotBarInvClassName, "hotbar-1", null!);

            // Create mock network util for inventory operations
            _invNetworkUtilMock = new Mock<IInventoryNetworkUtil>();
            _invNetworkUtilMock
                .Setup(u => u.GetFlipSlotsPacket(It.IsAny<InventoryBase>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(new object());

            // Setup API on inventories
            ApiMock = new Mock<ICoreAPI>();
            ApiMock.Setup(a => a.World).Returns(World);

            BackpackInventory.Api = ApiMock.Object;
            BackpackInventory.InvNetworkUtil = _invNetworkUtilMock.Object;
            HotbarInventory.Api = ApiMock.Object;
            HotbarInventory.InvNetworkUtil = _invNetworkUtilMock.Object;

            // Populate backpack inventory and set API on items
            if (backpackItems is not null)
            {
                for (int i = 0; i < backpackItems.Length && i < backpackSize; i++)
                {
                    backpackItems[i].SetApi(ApiMock.Object);
                    BackpackInventory[i].Itemstack = new ItemStack(backpackItems[i]);
                }
            }

            // Populate hotbar inventory and set API on items
            if (hotbarItems is not null)
            {
                for (int i = 0; i < hotbarItems.Length && i < hotbarSize; i++)
                {
                    hotbarItems[i].SetApi(ApiMock.Object);
                    HotbarInventory[i].Itemstack = new ItemStack(hotbarItems[i]);
                }
            }

            // Setup player with real inventories
            Player = new MockPlayer();
            Player.InventoryManagerMock
                .Setup(i => i.GetOwnInventory(GlobalConstants.backpackInvClassName))
                .Returns(BackpackInventory);
            Player.InventoryManagerMock
                .Setup(i => i.GetOwnInventory(GlobalConstants.hotBarInvClassName))
                .Returns(HotbarInventory);

            // Setup OpenInventory and CloseInventoryAndSync (required for StashMatchingItemsToContainer)
            Player.InventoryManagerMock
                .Setup(i => i.OpenInventory(It.IsAny<IInventory>()))
                .Returns(new object());
            Player.InventoryManagerMock
                .Setup(i => i.CloseInventoryAndSync(It.IsAny<IInventory>()));

            // Setup TryTransferTo to perform actual item transfer
            Player.InventoryManagerMock
                .Setup(i => i.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(), ref It.Ref<ItemStackMoveOperation>.IsAny))
                .Returns((ItemSlot source, ItemSlot target, ref ItemStackMoveOperation op) =>
                {
                    if (source.Empty || target.Itemstack?.Collectible?.Code != source.Itemstack?.Collectible?.Code && !target.Empty)
                    {
                        op.MovedQuantity = 0;
                        return null;
                    }

                    int toMove = Math.Min(op.RequestedQuantity, source.StackSize);
                    if (target.Empty)
                    {
                        target.Itemstack = source.TakeOut(toMove);
                        op.MovedQuantity = toMove;
                    }
                    else
                    {
                        // Merge into existing stack
                        int canFit = target.Itemstack.Collectible.MaxStackSize - target.StackSize;
                        int actualMove = Math.Min(toMove, canFit);
                        target.Itemstack.StackSize += actualMove;
                        source.Itemstack.StackSize -= actualMove;
                        if (source.Itemstack.StackSize <= 0)
                        {
                            source.Itemstack = null;
                        }
                        op.MovedQuantity = actualMove;
                    }
                    return new object(); // Return dummy packet
                });
        }

        /// <summary>
        /// Sets item in backpack at specified slot with custom stack size.
        /// </summary>
        public void SetBackpackItem(int slot, MockItem item, int stackSize = 1)
        {
            BackpackInventory[slot].Itemstack = new ItemStack(item, stackSize);
        }

        /// <summary>
        /// Sets item in hotbar at specified slot with custom stack size.
        /// </summary>
        public void SetHotbarItem(int slot, MockItem item, int stackSize = 1)
        {
            HotbarInventory[slot].Itemstack = new ItemStack(item, stackSize);
        }

        /// <summary>
        /// Gets total items remaining in player's backpack.
        /// </summary>
        public int GetBackpackTotalItems()
        {
            int total = 0;
            foreach (var slot in BackpackInventory)
            {
                if (!slot.Empty) total += slot.StackSize;
            }
            return total;
        }

        /// <summary>
        /// Gets total items remaining in player's hotbar.
        /// </summary>
        public int GetHotbarTotalItems()
        {
            int total = 0;
            foreach (var slot in HotbarInventory)
            {
                if (!slot.Empty) total += slot.StackSize;
            }
            return total;
        }
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
        var fixture = new TestFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var container = MockBlockEntityContainer.Empty();

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [playerItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(); // Empty player inventories

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [sharedItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);
        int initialContainerCount = container.GetNonEmptyStacks().Length;

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            hotbarItems: [sharedItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [sharedItem1],
            hotbarItems: [sharedItem2]);

        var containerItem = MockItem.CreateNonLightSource(id: 3);
        containerItem.Code = new AssetLocation("game", "shared-item");

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [playerItemA, playerItemB]);

        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, itemA }, { 1, itemB } }, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [matchingItem, nonMatchingItem]);

        var containerItem = MockItem.CreateNonLightSource(id: 3);
        containerItem.Code = new AssetLocation("game", "matching-item");

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player.Object,
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
        var fixture = new TestFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var crate = MockBlockEntityCrate.Empty(api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [playerItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(crateItem, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [playerItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(sharedItem, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [matchingItem, nonMatchingItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(crateItem, api: fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player.Object,
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
        var fixture = new TestFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player.Object,
            null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetStashableItems_EmptyContainer_ReturnsEmptySet()
    {
        // Arrange
        var fixture = new TestFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var container = MockBlockEntityContainer.Empty(api: fixture.Api);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [playerItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            backpackItems: [playerItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player.Object,
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

        var fixture = new TestFixture(
            hotbarItems: [hotbarItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player.Object,
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
        var fixture = new TestFixture();
        var container = MockBlockEntityContainer.Empty(api: fixture.Api);

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            BlockBehaviorAutoStashable.AutoStashToGenericContainer(
                fixture.World,
                fixture.Player.Object,
                container.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void AutoStashToCrate_NoExceptionOnEmptyInventories()
    {
        // Arrange
        var fixture = new TestFixture();
        var crate = MockBlockEntityCrate.Empty(api: fixture.Api);

        // Act & Assert
        var exception = Record.Exception(() =>
            BlockBehaviorAutoStashable.AutoStashToCrate(
                fixture.World,
                fixture.Player.Object,
                crate.Object));

        Assert.Null(exception);
    }

    #endregion
}
