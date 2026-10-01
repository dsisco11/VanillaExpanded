using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Exercises permission checks and container policy through registered block request handlers.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class ServerBlockRequestTests
{
    #region Public API
    #region Specialized targets and reachability
    /// <summary>Dispatches ore and fuel into their real bloomery input slots using the ore-dependent ratio.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BloomeryRequest_UsesInputClassificationAndRatio(bool allowed)
    {
        var test = new BloomeryCase();
        using var request = new ServerRequestCase(test.Fixture);
        var ore = test.Source(test.Ore, 5);
        var fuel = test.Source(test.Fuel, 5, 1);
        var invalid = test.Source(test.Invalid, 1, 9, true);
        var block = new Block();
        block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(test.Target);
        request.Claims.Setup(value => value.TryAccess(request.Player.Object, request.Position, EnumBlockAccessFlags.Use)).Returns(allowed);
        var before = test.Snapshot();

        request.BlockRequest();

        test.AssertSlot(ore, test.Ore, allowed ? 0 : 5);
        test.AssertSlot(fuel, test.Fuel, allowed ? 2 : 5);
        test.AssertSlot(invalid, test.Invalid, 1);
        test.AssertSlot(test.Target.TestInventory[1], test.Ore, allowed ? 5 : 0);
        test.AssertSlot(test.Target.TestInventory[0], test.Fuel, allowed ? 3 : 0);
        test.Verify(before, allowed, allowed ? [ore, fuel, test.Target.TestInventory[0], test.Target.TestInventory[1]] : []);
        request.AssertNoSyntheticTransfer();
    }

    /// <summary>Preserves real crate suitability and session behavior while dispatching a normal matching request.</summary>
    [Fact]
    public void InitializedCrateRequest_TransfersThroughEngineInventory()
    {
        var fixture = VsTestFixture.Server();
        using var request = new ServerRequestCase(fixture);
        var target = new InitializedCrate(fixture.Api);
        var item = new MockItem(1, api: fixture.Api) { Code = new AssetLocation("game:server-crate-item"), MaxStackSize = 64 };
        target.Inventory[0].Itemstack = Stack(item, 10);
        fixture.BackpackInventory[0].Itemstack = Stack(item, 3);
        var block = new Block();
        block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(target);
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, target.Inventory);
        int dirtyBefore = target.DirtyCalls;

        request.BlockRequest();

        InventorySnapshot.AssertStack(target.Inventory[0], item, 13);
        Assert.True(fixture.BackpackInventory[0].Empty);
        Assert.Equal("preserved", target.Inventory[0].Itemstack!.Attributes.GetString("fixture"));
        before.AssertUnchangedExcept(fixture.BackpackInventory[0], target.Inventory[0]);
        before.AssertConserved();
        // The initialized engine inventory synchronizes its SlotModified event before AutoStash's final dirty callback.
        Assert.Equal(dirtyBefore + 2, target.DirtyCalls);
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(target.Inventory), Times.Once);
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(target.Inventory), Times.Once);
        request.AssertNoSyntheticTransfer();
    }

    /// <summary>Preserves eligible inventories when the real engine rejects player reachability before claims are queried.</summary>
    [Fact]
    public void OutOfRangeRequest_DoesNotConsultClaimsOrMutate()
    {
        var fixture = VsTestFixture.Server();
        using var request = new ServerRequestCase(fixture);
        request.PlayerEntity.Pos.SetPos(100, 0, 0);
        var item = new MockItem(1, api: fixture.Api) { Code = new AssetLocation("game:server-range-item") };
        var inventory = new InventoryGeneric(1, "server", "target", null!) { Api = fixture.Api };
        inventory[0].Itemstack = Stack(item, 10);
        fixture.BackpackInventory[0].Itemstack = Stack(item, 3);
        var target = new Mock<BlockEntityContainer>();
        target.SetupGet(value => value.Inventory).Returns(inventory);
        var block = new Block();
        block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(target.Object);
        Assert.NotEmpty(BlockBehaviorAutoStashable.GetStashableItems(fixture.Player, target.Object));
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, inventory);

        request.BlockRequest();

        before.AssertUnchangedExcept();
        before.AssertConserved();
        request.Claims.Verify(value => value.TryAccess(It.IsAny<IPlayer>(), It.IsAny<Vintagestory.API.MathTools.BlockPos>(), It.IsAny<EnumBlockAccessFlags>()), Times.Never);
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
        request.AssertNoSyntheticTransfer();
    }

    #endregion
    #region Container policy and current state
    /// <summary>Reevaluates changed target type and remaining capacity after a positive client eligibility check.</summary>
    [Theory]
    [InlineData("type", 0)]
    [InlineData("partial", 2)]
    [InlineData("full", 0)]
    public void TargetChangesBeforeReceipt_UsesCurrentInventory(string change, int moved)
    {
        var fixture = VsTestFixture.Server();
        using var request = new ServerRequestCase(fixture);
        var item = new MockItem(1, api: fixture.Api) { Code = new AssetLocation("game:server-current-item"), MaxStackSize = 64 };
        var other = new MockItem(2, api: fixture.Api) { Code = new AssetLocation("game:server-other-item"), MaxStackSize = 64 };
        var inventory = new InventoryGeneric(change == "type" ? 2 : 1, "server", "target", null!) { Api = fixture.Api, InvNetworkUtil = fixture.InvNetworkUtilMock.Object };
        inventory[0].Itemstack = Stack(item, 10);
        fixture.BackpackInventory[0].Itemstack = Stack(item, 3);
        var target = new Mock<BlockEntityContainer>();
        target.SetupGet(value => value.Inventory).Returns(inventory);
        target.SetupGet(value => value.InventoryClassName).Returns("server");
        target.Object.Pos = request.Position;
        var block = new Block();
        block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(target.Object);
        Assert.Contains(item.Id, BlockBehaviorAutoStashable.GetStashableItems(fixture.Player, target.Object));
        inventory[0].Itemstack = Stack(change == "type" ? other : item, change == "type" ? 10 : change == "partial" ? 62 : 64);
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, inventory);

        request.BlockRequest();

        InventorySnapshot.AssertStack(fixture.BackpackInventory[0], item, 3 - moved);
        InventorySnapshot.AssertStack(inventory[0], change == "type" ? other : item, change == "type" ? 10 : 64);
        Assert.Equal("preserved", inventory[0].Itemstack!.Attributes.GetString("fixture"));
        Assert.Equal("preserved", fixture.BackpackInventory[0].Itemstack!.Attributes.GetString("fixture"));
        before.AssertUnchangedExcept(moved > 0 ? [fixture.BackpackInventory[0], inventory[0]] : []);
        before.AssertConserved();
        target.Verify(value => value.MarkDirty(false, null!), Times.Exactly(moved > 0 ? 1 : 0));
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Exactly(moved > 0 ? 1 : 0));
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(inventory), Times.Exactly(moved > 0 ? 1 : 0));
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(inventory), Times.Exactly(moved > 0 ? 1 : 0));
        request.AssertNoSyntheticTransfer();
    }
    /// <summary>Distinguishes crate first-type policy from ordinary matching-type policy and denied access.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void MatchingTypes_DispatchAndPermissionDetermineMutation(bool crate, bool allowed)
    {
        var fixture = VsTestFixture.Server();
        using var request = new ServerRequestCase(fixture);
        var first = new MockItem(1, api: fixture.Api) { Code = new AssetLocation("game:server-first"), MaxStackSize = 64 };
        var second = new MockItem(2, api: fixture.Api) { Code = new AssetLocation("game:server-second"), MaxStackSize = 64 };
        var inventory = new InventoryGeneric(2, "server", "target", null!) { Api = fixture.Api, InvNetworkUtil = fixture.InvNetworkUtilMock.Object };
        inventory[0].Itemstack = Stack(first, 10);
        inventory[1].Itemstack = Stack(second, 10);
        fixture.BackpackInventory[0].Itemstack = Stack(first, 3);
        fixture.HotbarInventory[1].Itemstack = Stack(second, 4);
        // The mixed-type crate substitutes only storage to make its separate first-type dispatch policy observable.
        var target = new Mock<BlockEntityContainer>();
        var crateTarget = new Mock<BlockEntityCrate>();
        target.Object.Pos = request.Position;
        crateTarget.Object.Pos = request.Position;
        crateTarget.SetupGet(value => value.Inventory).Returns(inventory);
        crateTarget.SetupGet(value => value.InventoryClassName).Returns("server");
        crateTarget.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
        target.SetupGet(value => value.Inventory).Returns(inventory);
        target.SetupGet(value => value.InventoryClassName).Returns("server");
        target.Setup(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()));
        var block = new Block { BlockBehaviors = [] };
        block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(crate ? crateTarget.Object : target.Object);
        request.Claims.Setup(value => value.TryAccess(request.Player.Object, request.Position, EnumBlockAccessFlags.Use)).Returns(allowed);
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, inventory);

        request.BlockRequest();

        InventorySnapshot.AssertStack(inventory[0], first, allowed ? 13 : 10);
        InventorySnapshot.AssertStack(inventory[1], second, allowed && !crate ? 14 : 10);
        if (allowed) Assert.True(fixture.BackpackInventory[0].Empty);
        if (allowed && !crate) Assert.True(fixture.HotbarInventory[1].Empty);
        else InventorySnapshot.AssertStack(fixture.HotbarInventory[1], second, 4);
        before.AssertUnchangedExcept(allowed ? (crate ? [fixture.BackpackInventory[0], inventory[0]] : [fixture.BackpackInventory[0], fixture.HotbarInventory[1], inventory[0], inventory[1]]) : []);
        before.AssertConserved();
        Assert.Equal("preserved", inventory[0].Itemstack!.Attributes.GetString("fixture"));
        Assert.Equal("preserved", inventory[1].Itemstack!.Attributes.GetString("fixture"));
        if (crate) crateTarget.Verify(value => value.MarkDirty(false, null!), Times.Exactly(allowed ? 1 : 0));
        else target.Verify(value => value.MarkDirty(false, null!), Times.Exactly(allowed ? 1 : 0));
        crateTarget.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Exactly(allowed && crate ? 1 : 0));
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Exactly(allowed && !crate ? 1 : 0));
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(inventory), Times.Exactly(allowed ? 1 : 0));
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(inventory), Times.Exactly(allowed ? 1 : 0));
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Exactly(allowed ? 1 : 0));
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Exactly(allowed ? 1 : 0));
        request.Claims.Verify(value => value.TryAccess(request.Player.Object, request.Position, EnumBlockAccessFlags.Use), Times.Once);
        request.AssertNoSyntheticTransfer();
    }

    /// <summary>Preserves otherwise eligible sources when the block behavior or target entity is absent.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingBehaviorOrTarget_DoesNotMutate(bool missingBehavior)
    {
        var fixture = VsTestFixture.Server();
        using var request = new ServerRequestCase(fixture);
        var item = new MockItem(1, api: fixture.Api) { Code = new AssetLocation("game:server-item") };
        var inventory = new InventoryGeneric(1, "server", "target", null!) { Api = fixture.Api };
        inventory[0].Itemstack = Stack(item, 10);
        fixture.BackpackInventory[0].Itemstack = Stack(item, 3);
        var target = new Mock<BlockEntityContainer>();
        target.SetupGet(value => value.Inventory).Returns(inventory);
        var block = new Block { BlockBehaviors = [] };
        if (!missingBehavior) block.CollectibleBehaviors = [new BlockBehaviorAutoStashable(block)];
        request.Accessor.Setup(value => value.GetBlock(request.Position)).Returns(block);
        request.Accessor.Setup(value => value.GetBlockEntity(request.Position)).Returns(missingBehavior ? target.Object : null!);
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, inventory);

        request.BlockRequest();

        before.AssertUnchangedExcept();
        before.AssertConserved();
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
        request.AssertNoSyntheticTransfer();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates attributed stacks to catch changes to item data during server transfers.</summary>
    private static ItemStack Stack(CollectibleObject item, int quantity)
    {
        var stack = new ItemStack(item, quantity);
        stack.Attributes.SetString("fixture", "preserved");
        return stack;
    }
    #endregion
}
