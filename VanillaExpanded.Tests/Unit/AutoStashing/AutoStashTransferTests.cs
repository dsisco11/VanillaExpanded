using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.AutoStashing.Targets;
using Moq;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;

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
[Collection("AutoStash")]
public class AutoStashTransferTests : IDisposable
{
    private readonly VanillaExpanded.Tests.Unit.AutoStashing.Support.AutoStashTestScope scope = new();

    #region Public API

    /// <summary>Restores AutoStash settings after each test.</summary>
    public void Dispose() => scope.Dispose();
    /// <summary>Verifies the session or capacity contract.</summary>
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    public void TransferService_ManagesOnlySessionsItOwns(bool manageSession, bool alreadyOpen, int expectedLifecycleCalls)
    {
        var sharedItem = MockItem.CreateNonLightSource(1);
        sharedItem.MaxStackSize = 64;
        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, sharedItem, stackSize: 7);
        fixture.WithHotbarSlot(0, sharedItem, stackSize: 3);
        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);
        int initialQuantity = container.Inventory[0].StackSize;
        fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories)
            .Returns(alreadyOpen ? new List<IInventory> { container.Inventory } : new List<IInventory>());

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        var target = new Mock<InventoryAutoStashTarget>(container.Inventory) { CallBase = true };
        if (!manageSession)
        {
            target.Setup(value => value.Acquire(It.IsAny<IPlayerInventoryManager>()));
            target.Setup(value => value.Release(It.IsAny<IPlayerInventoryManager>()));
        }
        int moved = AutoStashService.Execute(
            fixture.World, fixture.Player, "test-player", target.Object,
            new BlockPos(0), "test-container", new MatchingContentsPolicy(_ => true)).MovedQuantity;

        Assert.Equal(10, moved);
        Assert.Equal(initialQuantity + 10, container.Inventory[0].StackSize);
        Assert.True(fixture.BackpackInventory[0].Empty);
        Assert.True(fixture.HotbarInventory[0].Empty);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Exactly(expectedLifecycleCalls));
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Exactly(expectedLifecycleCalls));
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], fixture.HotbarInventory[0], container.Inventory[0]);
        before.AssertConserved();
    }

    /// <summary>Verifies the session or capacity contract.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferService_NoMatchingItemsOrCapacity_DoesNotOpenSession(bool full)
    {
        var sharedItem = MockItem.CreateNonLightSource(1);
        sharedItem.MaxStackSize = 64;
        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, sharedItem, stackSize: 7);
        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, sharedItem } }, totalSlots: 1, api: fixture.Api);
        if (full)
        {
            container.Inventory[0].Itemstack!.StackSize = 64;
        }

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        int moved = AutoStashService.Execute(
            fixture.World, fixture.Player, "test-player", new InventoryAutoStashTarget(container.Inventory),
            new BlockPos(0), "test-container", new MatchingContentsPolicy(_ => full)).MovedQuantity;

        Assert.Equal(0, moved);
        Assert.Equal(7, fixture.BackpackInventory[0].StackSize);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Never);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
    }

    /// <summary>Verifies RefreshWorkspaceSlots RepeatedReloads UpdateContentsWithoutReplacingSlots.</summary>
    [Fact]
    public void RefreshWorkspaceSlots_RepeatedReloads_UpdateContentsWithoutReplacingSlots()
    {
        var inventory = new InventoryGeneric(2, "mountedbaginv", "test", null!);
        ItemSlot firstSlot = inventory[0];
        ItemSlot secondSlot = inventory[1];
        var originalStack = new ItemStack(MockItem.CreateNonLightSource(1));
        var updatedStack = new ItemStack(MockItem.CreateNonLightSource(2));
        firstSlot.Itemstack = originalStack;
        secondSlot.Itemstack = originalStack.Clone();

        AttachedBagAutoStashTarget.RefreshWorkspaceSlots(inventory,
            new ItemSlot[] { new DummySlot(updatedStack), new DummySlot(null) });

        Assert.Same(firstSlot, inventory[0]);
        Assert.Same(secondSlot, inventory[1]);
        Assert.Same(updatedStack, firstSlot.Itemstack);
        Assert.True(secondSlot.Empty);

        AttachedBagAutoStashTarget.RefreshWorkspaceSlots(inventory,
            new ItemSlot[] { new DummySlot(null), new DummySlot(originalStack) });

        Assert.Same(firstSlot, inventory[0]);
        Assert.Same(secondSlot, inventory[1]);
        Assert.True(firstSlot.Empty);
        Assert.Same(originalStack, secondSlot.Itemstack);
    }


    #region Timing Constants Tests

    /// <summary>Verifies StashDelaySeconds HasExpectedDefaultValue.</summary>
    [Fact]
    public void StashDelaySeconds_HasExpectedDefaultValue()
    {
        // Arrange - Create instance to access the instance field
        var behavior = new BlockBehaviorAutoStashable(null!);

        // Assert - Default value when config is null
        Assert.Equal(0.5f, behavior.StashDelaySeconds);
    }

    /// <summary>Verifies PreStashGracePeriodSeconds HasExpectedValue.</summary>
    [Fact]
    public void PreStashGracePeriodSeconds_HasExpectedValue()
    {
        // Assert
        Assert.Equal(0.1f, BlockBehaviorAutoStashable.PreStashGracePeriodSeconds);
    }

    /// <summary>Verifies PostStashGracePeriodSeconds HasExpectedValue.</summary>
    [Fact]
    public void PostStashGracePeriodSeconds_HasExpectedValue()
    {
        // Assert
        Assert.Equal(0.4f, BlockBehaviorAutoStashable.PostStashGracePeriodSeconds);
    }

    /// <summary>Verifies TotalStashDuration SumOfDelayAndGracePeriods.</summary>
    [Fact]
    public void TotalStashDuration_SumOfDelayAndGracePeriods()
    {
        // Arrange
        var behavior = new BlockBehaviorAutoStashable(null!);
        var expectedTotal = behavior.StashDelaySeconds + BlockBehaviorAutoStashable.PostStashGracePeriodSeconds;

        // Assert - Total interaction time is stashDelay + postStashGracePeriod
        Assert.Equal(0.9f, expectedTotal);
    }

    /// <summary>Verifies PreStashGracePeriod IsLessThanStashDelay.</summary>
    [Fact]
    public void PreStashGracePeriod_IsLessThanStashDelay()
    {
        // Arrange
        var behavior = new BlockBehaviorAutoStashable(null!);

        // The pre-stash grace period should always be less than the stash delay
        // to ensure the UI shows before stashing occurs
        Assert.True(BlockBehaviorAutoStashable.PreStashGracePeriodSeconds < behavior.StashDelaySeconds);
    }

    /// <summary>Verifies TimingConstants ArePositive.</summary>
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

    /// <summary>Verifies AutoStashToGenericContainer EmptyContainer ReturnsFalse.</summary>
    [Fact]
    public void AutoStashToGenericContainer_EmptyContainer_ReturnsFalse()
    {
        // Arrange - Container is empty, player has items
        var fixture = CreateFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var container = MockBlockEntityContainer.Empty();

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return false (empty container has no item types to match)
        Assert.False(result);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Never);
    }

    /// <summary>Verifies AutoStashToGenericContainer NoMatchingItems ReturnsFalse.</summary>
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

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return false (no matching item types)
        Assert.False(result);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Never);
    }

    /// <summary>Verifies GetStashableItems FullMatchingContainer ReturnsEmptySet.</summary>
    [Fact]
    public void GetStashableItems_FullMatchingContainer_ReturnsEmptySet()
    {
        // Arrange - The container has the matching item type but no remaining stack space.
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");
        sharedItem.MaxStackSize = 1;

        var fixture = CreateFixture(
            backpackItems: [sharedItem]);
        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, sharedItem } },
            totalSlots: 1,
            api: fixture.Api);
        container.Inventory[0].Itemstack!.StackSize = sharedItem.MaxStackSize;

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        HashSet<int> result = BlockBehaviorAutoStashable.GetStashableItems(
            fixture.Player,
            container.Object);

        // Assert
        Assert.Empty(result);
        fixture.InventoryManagerMock.Verify(
            inventoryManager => inventoryManager.OpenInventory(container.Inventory),
            Times.Never);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
    }

    /// <summary>Verifies AutoStashToGenericContainer PlayerInventoryEmpty ReturnsFalse.</summary>
    [Fact]
    public void AutoStashToGenericContainer_PlayerInventoryEmpty_ReturnsFalse()
    {
        // Arrange - Container has items, player inventories are empty
        var containerItem = MockItem.CreateNonLightSource(id: 1);
        containerItem.Code = new AssetLocation("game", "test-item");

        var fixture = CreateFixture(); // Empty player inventories

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return false (player has no items to stash)
        Assert.False(result);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Never);
    }

    #endregion

    #region AutoStashToGenericContainer - Successful Stash Tests

    /// <summary>Verifies AutoStashToGenericContainer MatchingItemsInBackpack StashesToContainer.</summary>
    [Fact]
    public void AutoStashToGenericContainer_MatchingItemsInBackpack_StashesToContainer()
    {
        // Arrange - Container and player both have same item type
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var fixture = CreateFixture(
            backpackItems: [sharedItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);


        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Should return true and stash items
        Assert.True(result);
        // Player's backpack should be empty after stash
        Assert.True(fixture.BackpackInventory[0].Empty);
        container.BlockEntityMock.Verify(
            blockEntity => blockEntity.MarkDirty(false, null!),
            Times.Once);
        fixture.InventoryManagerMock.Verify(
            inventoryManager => inventoryManager.CloseInventoryAndSync(container.Inventory),
            Times.Once);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], container.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(container.Inventory[0], sharedItem, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer DoesNotUseClientTransferApi.</summary>
    [Fact]
    public void AutoStashToGenericContainer_DoesNotUseClientTransferApi()
    {
        // Arrange
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var fixture = CreateFixture(
            backpackItems: [sharedItem]);
        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);

        fixture.InventoryManagerMock
            .Setup(inventoryManager => inventoryManager.TryTransferTo(
                It.IsAny<ItemSlot>(),
                It.IsAny<ItemSlot>(),
                ref It.Ref<ItemStackMoveOperation>.IsAny))
            .Throws(new InvalidOperationException("Client transfer API must not be used server-side."));

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
        fixture.InventoryManagerMock.Verify(
            inventoryManager => inventoryManager.TryTransferTo(
                It.IsAny<ItemSlot>(),
                It.IsAny<ItemSlot>(),
                ref It.Ref<ItemStackMoveOperation>.IsAny),
            Times.Never);
        fixture.InventoryManagerMock.Verify(
            inventoryManager => inventoryManager.CloseInventoryAndSync(container.Inventory),
            Times.Once);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], container.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(container.Inventory[0], sharedItem, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer MatchingItemsInHotbar StashesToContainer.</summary>
    [Fact]
    public void AutoStashToGenericContainer_MatchingItemsInHotbar_StashesToContainer()
    {
        // Arrange - Container has item, player has matching item in hotbar
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var fixture = CreateFixture(
            hotbarItems: [sharedItem]);

        var container = MockBlockEntityContainer.WithItems(fixture.Api, sharedItem);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.HotbarInventory[0].Empty);
        before.AssertUnchangedExcept(fixture.HotbarInventory[0], container.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(container.Inventory[0], sharedItem, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer ItemsInBothInventories StashesBoth.</summary>
    [Fact]
    public void AutoStashToGenericContainer_ItemsInBothInventories_StashesBoth()
    {
        // Arrange - Player has matching items in both backpack and hotbar
        var sharedItem1 = MockItem.CreateNonLightSource(id: 1);
        sharedItem1.Code = new AssetLocation("game", "shared-item");

        var sharedItem2 = sharedItem1;
        sharedItem2.Code = new AssetLocation("game", "shared-item"); // Same code

        var fixture = CreateFixture(
            backpackItems: [sharedItem1],
            hotbarItems: [sharedItem2]);

        var containerItem = sharedItem1;

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert - Both inventories should be emptied
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
        Assert.True(fixture.HotbarInventory[0].Empty);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], fixture.HotbarInventory[0], container.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(container.Inventory[0], sharedItem1, 3);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer FirstSlotPartiallyFull UsesAdditionalSlot.</summary>
    [Fact]
    public void AutoStashToGenericContainer_FirstSlotPartiallyFull_UsesAdditionalSlot()
    {
        // Arrange
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");
        sharedItem.MaxStackSize = 64;

        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, sharedItem, stackSize: 10);

        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, sharedItem } },
            totalSlots: 2,
            api: fixture.Api);
        container.Inventory[0].Itemstack!.StackSize = 63;

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.Equal(64, container.Inventory[0].StackSize);
        Assert.Equal(9, container.Inventory[1].StackSize);
        Assert.True(fixture.BackpackInventory[0].Empty);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], container.Inventory[0], container.Inventory[1]);
        before.AssertConserved();
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer FirstMatchingSlotFull UsesAvailableMatchingSlot.</summary>
    [Fact]
    public void AutoStashToGenericContainer_FirstMatchingSlotFull_UsesAvailableMatchingSlot()
    {
        // Arrange
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");
        sharedItem.MaxStackSize = 64;

        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, sharedItem, stackSize: 10);

        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, sharedItem }, { 1, sharedItem } },
            totalSlots: 3,
            api: fixture.Api);
        container.Inventory[0].Itemstack!.StackSize = 64;
        container.Inventory[1].Itemstack!.StackSize = 60;

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.Equal(64, container.Inventory[0].StackSize);
        Assert.Equal(64, container.Inventory[1].StackSize);
        Assert.Equal(6, container.Inventory[2].StackSize);
        Assert.True(fixture.BackpackInventory[0].Empty);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], container.Inventory[0], container.Inventory[1], container.Inventory[2]);
        before.AssertConserved();
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer MultipleMatchingTypes StashesAll.</summary>
    [Fact]
    public void AutoStashToGenericContainer_MultipleMatchingTypes_StashesAll()
    {
        // Arrange - Container has multiple item types, player has all of them
        var itemA = MockItem.CreateNonLightSource(id: 1);
        itemA.Code = new AssetLocation("game", "item-a");

        var itemB = MockItem.CreateNonLightSource(id: 2);
        itemB.Code = new AssetLocation("game", "item-b");

        var playerItemA = itemA;
        playerItemA.Code = new AssetLocation("game", "item-a");

        var playerItemB = itemB;
        playerItemB.Code = new AssetLocation("game", "item-b");

        var fixture = CreateFixture(
            backpackItems: [playerItemA, playerItemB]);

        var container = MockBlockEntityContainer.WithItems(
            new Dictionary<int, MockItem> { { 0, itemA }, { 1, itemB } }, api: fixture.Api);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToGenericContainer(
            fixture.World,
            fixture.Player,
            container.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
        Assert.True(fixture.BackpackInventory[1].Empty);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], fixture.BackpackInventory[1], container.Inventory[0], container.Inventory[1]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(container.Inventory[0], itemA, 2);
        InventorySnapshot.AssertStack(container.Inventory[1], itemB, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    /// <summary>Verifies AutoStashToGenericContainer OnlyMatchingTypesStashed NonMatchingRemains.</summary>
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

        var containerItem = matchingItem;

        var container = MockBlockEntityContainer.WithItems(fixture.Api, containerItem);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

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
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], container.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(container.Inventory[0], matchingItem, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Once);
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    #endregion

    #region AutoStashToCrate - Empty/No Match Tests

    /// <summary>Verifies AutoStashToCrate EmptyCrate ReturnsFalse.</summary>
    [Fact]
    public void AutoStashToCrate_EmptyCrate_ReturnsFalse()
    {
        // Arrange - Crate is empty, player has items
        var fixture = CreateFixture(
            backpackItems: [MockItem.CreateNonLightSource(id: 1)]);

        var crate = MockBlockEntityCrate.Empty(api: fixture.Api);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, crate.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert - Should return false (empty crate has no accepted item type)
        Assert.False(result);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        crate.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(crate.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(crate.Inventory), Times.Never);
    }

    /// <summary>Verifies AutoStashToCrate PlayerHasDifferentItems ReturnsFalse.</summary>
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

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, crate.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert
        Assert.False(result);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        crate.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(crate.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(crate.Inventory), Times.Never);
    }

    #endregion

    #region AutoStashToCrate - Successful Stash Tests

    /// <summary>Verifies AutoStashToCrate MatchingItems StashesToCrate.</summary>
    [Fact]
    public void AutoStashToCrate_MatchingItems_StashesToCrate()
    {
        // Arrange - Crate and player have same item type
        var sharedItem = MockItem.CreateNonLightSource(id: 1);
        sharedItem.Code = new AssetLocation("game", "shared-item");

        var playerItem = sharedItem;

        var fixture = CreateFixture(
            backpackItems: [playerItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(sharedItem, api: fixture.Api);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, crate.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty);
        crate.BlockEntityMock.Verify(
            blockEntity => blockEntity.MarkDirty(false, null!),
            Times.Once);
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], crate.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(crate.Inventory[0], sharedItem, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(crate.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(crate.Inventory), Times.Once);
    }

    /// <summary>Verifies AutoStashToCrate MultipleItemTypes OnlyMatchingTypeStashed.</summary>
    [Fact]
    public void AutoStashToCrate_MultipleItemTypes_OnlyMatchingTypeStashed()
    {
        // Arrange - Player has multiple item types, crate only accepts one
        var crateItem = MockItem.CreateNonLightSource(id: 1);
        crateItem.Code = new AssetLocation("game", "crate-accepted");

        var matchingItem = crateItem;

        var nonMatchingItem = MockItem.CreateNonLightSource(id: 3);
        nonMatchingItem.Code = new AssetLocation("game", "not-accepted");

        var fixture = CreateFixture(
            backpackItems: [matchingItem, nonMatchingItem]);

        var crate = MockBlockEntityCrate.WithSingleItemType(crateItem, api: fixture.Api);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, crate.Inventory);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToCrate(
            fixture.World,
            fixture.Player,
            crate.Object);

        // Assert - Only matching item stashed
        Assert.True(result);
        Assert.True(fixture.BackpackInventory[0].Empty); // Matching stashed
        Assert.False(fixture.BackpackInventory[1].Empty); // Non-matching remains
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], crate.Inventory[0]);
        before.AssertConserved();
        InventorySnapshot.AssertStack(crate.Inventory[0], crateItem, 2);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(crate.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(crate.Inventory), Times.Once);
        crate.BlockEntityMock.Verify(entity => entity.MarkDirty(false, null!), Times.Once);
    }

    #endregion

    #region Bloomery Transfer Tests

    /// <summary>Verifies AutoStashToBloomery EmptyFuelSlot MovesUpToFuelCapacity.</summary>
    [Fact]
    public void AutoStashToBloomery_EmptyFuelSlot_MovesUpToFuelCapacity()
    {
        // Arrange
        var fuel = MockItem.CreateBloomeryFuel(id: 1);
        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, fuel, stackSize: 10);
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.Setup(world => world.BlockAccessor).Returns(blockAccessor.Object);
        var bloomery = MockBlockEntityBloomery.Empty(new BlockPos(0), fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToBloomery(
            fixture.World,
            fixture.Player,
            bloomery.Object);

        // Assert
        Assert.True(result);
        Assert.Equal(6, bloomery.FuelSlot.StackSize);
        Assert.Equal(4, fixture.BackpackInventory[0].StackSize);
    }

    /// <summary>Verifies AutoStashToBloomery OreWithRatioTwo MovesTwelveItems.</summary>
    [Fact]
    public void AutoStashToBloomery_OreWithRatioTwo_MovesTwelveItems()
    {
        // Arrange
        var ore = MockItem.CreateBloomeryOre(id: 2, smeltedRatio: 2);
        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, ore, stackSize: 20);
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.Setup(world => world.BlockAccessor).Returns(blockAccessor.Object);
        var bloomery = MockBlockEntityBloomery.Empty(new BlockPos(0), fixture.Api);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToBloomery(
            fixture.World,
            fixture.Player,
            bloomery.Object);

        // Assert
        Assert.True(result);
        Assert.Equal(12, bloomery.OreSlot.StackSize);
        Assert.Equal(8, fixture.BackpackInventory[0].StackSize);
    }

    /// <summary>Verifies AutoStashToBloomery ExistingOreHasZeroRatio UsesMinimumRatio.</summary>
    [Fact]
    public void AutoStashToBloomery_ExistingOreHasZeroRatio_UsesMinimumRatio()
    {
        // Arrange
        var ore = MockItem.CreateBloomeryOre(id: 2, smeltedRatio: 0);
        var fuel = MockItem.CreateBloomeryFuel(id: 1);
        var fixture = CreateFixture();
        fixture.WithBackpackSlot(0, fuel, stackSize: 10);
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.Setup(world => world.BlockAccessor).Returns(blockAccessor.Object);
        var bloomery = MockBlockEntityBloomery
            .Empty(new BlockPos(0), fixture.Api)
            .WithOre(ore, stackSize: 6);

        // Act
        bool result = BlockBehaviorAutoStashable.AutoStashToBloomery(
            fixture.World,
            fixture.Player,
            bloomery.Object);

        // Assert
        Assert.True(result);
        Assert.Equal(6, bloomery.FuelSlot.StackSize);
        Assert.Equal(4, fixture.BackpackInventory[0].StackSize);
    }

    #endregion

    #region GetStashableItems Tests

    /// <summary>Verifies GetStashableItems NullContainer ReturnsEmptySet.</summary>
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

    /// <summary>Verifies GetStashableItems EmptyContainer ReturnsEmptySet.</summary>
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

    /// <summary>Verifies GetStashableItems MatchingItems ReturnsIntersection.</summary>
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

    /// <summary>Verifies GetStashableItems NoOverlap ReturnsEmptySet.</summary>
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

    /// <summary>Verifies GetStashableItems ItemsInHotbar IncludedInResult.</summary>
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

    /// <summary>Verifies AutoStashToGenericContainer NoExceptionOnEmptyInventories.</summary>
    [Fact]
    public void AutoStashToGenericContainer_NoExceptionOnEmptyInventories()
    {
        // Arrange - All inventories empty
        var fixture = CreateFixture();
        var container = MockBlockEntityContainer.Empty(api: fixture.Api);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, container.Inventory);

        // Act & Assert - Should not throw
        var exception = Record.Exception(() =>
            BlockBehaviorAutoStashable.AutoStashToGenericContainer(
                fixture.World,
                fixture.Player,
                container.Object));

        Assert.Null(exception);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        container.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(container.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(container.Inventory), Times.Never);
    }

    /// <summary>Verifies AutoStashToCrate NoExceptionOnEmptyInventories.</summary>
    [Fact]
    public void AutoStashToCrate_NoExceptionOnEmptyInventories()
    {
        // Arrange
        var fixture = CreateFixture();
        var crate = MockBlockEntityCrate.Empty(api: fixture.Api);

        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, crate.Inventory);

        // Act & Assert
        var exception = Record.Exception(() =>
            BlockBehaviorAutoStashable.AutoStashToCrate(
                fixture.World,
                fixture.Player,
                crate.Object));

        Assert.Null(exception);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        crate.BlockEntityMock.Verify(entity => entity.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(crate.Inventory), Times.Never);
        fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(crate.Inventory), Times.Never);
    }

    #endregion
    #endregion

    #region Private

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
}
