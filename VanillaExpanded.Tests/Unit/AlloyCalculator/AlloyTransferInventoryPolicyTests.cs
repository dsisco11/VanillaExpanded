using Moq;
using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies fresh storage discovery and directional transfer permissions.</summary>
[Trait("Category", "Unit")]
public class AlloyTransferInventoryPolicyTests
{
    #region Public API

    #region Inventory Discovery

    /// <summary>Verifies player-first ordering, supported owners, and repeated inventory exclusion.</summary>
    [Fact]
    public void TryCollect_OpenedStorage_AppendsOnceInOpenedOrder()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var accessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(accessor.Object);
        InventoryGeneric target = CreateInventory(fixture, 9);
        InventoryGeneric chest = CreateInventory(fixture, 1);
        InventoryGeneric typed = CreateInventory(fixture, 2);
        InventoryGeneric crate = CreateInventory(fixture, 3);
        var chestOwner = new Mock<BlockEntityGenericContainer>();
        chestOwner.SetupGet(owner => owner.Inventory).Returns(chest);
        var typedOwner = new Mock<BlockEntityGenericTypedContainer>();
        typedOwner.SetupGet(owner => owner.Inventory).Returns(typed);
        var crateOwner = new Mock<BlockEntityCrate>();
        crateOwner.SetupGet(owner => owner.Inventory).Returns(crate);
        accessor.Setup(a => a.GetBlockEntity(chest.Pos)).Returns(chestOwner.Object);
        accessor.Setup(a => a.GetBlockEntity(typed.Pos)).Returns(typedOwner.Object);
        accessor.Setup(a => a.GetBlockEntity(crate.Pos)).Returns(crateOwner.Object);
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories)
            .Returns([target, chest, fixture.BackpackInventory, typed, chest, crate, fixture.HotbarInventory]);

        Assert.True(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, target, out var slots));

        Assert.Equal(fixture.BackpackInventory.Concat(fixture.HotbarInventory).Concat(chest).Concat(typed).Concat(crate), slots);
    }

    /// <summary>Verifies machines, owner mismatches, equipment, and unresolved owners cannot join the pool.</summary>
    [Fact]
    public void TryCollect_UnsupportedOrUnownedInventories_AreExcluded()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var accessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(accessor.Object);
        InventoryGeneric target = CreateInventory(fixture, 9);
        InventoryGeneric machine = CreateInventory(fixture, 1);
        InventoryGeneric mismatch = CreateInventory(fixture, 2);
        InventoryGeneric unresolved = CreateInventory(fixture, 3);
        var wrongOwner = new Mock<BlockEntityGenericContainer>();
        wrongOwner.SetupGet(owner => owner.Inventory).Returns(target);
        accessor.Setup(a => a.GetBlockEntity(machine.Pos)).Returns(new BlockEntityFirepit());
        accessor.Setup(a => a.GetBlockEntity(mismatch.Pos)).Returns(wrongOwner.Object);
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories)
            .Returns([target, machine, mismatch, unresolved, fixture.OffhandInventory]);

        Assert.True(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, target, out var slots));

        Assert.Equal(fixture.BackpackInventory.Concat(fixture.HotbarInventory), slots);
    }

    /// <summary>Verifies duplicate slot references and aliases of target slots cannot enter the pool.</summary>
    [Fact]
    public void TryCollect_AliasedSlots_DeduplicatesAndExcludesTargetAliases()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        InventoryGeneric target = CreateInventory(fixture, 9);
        fixture.HotbarInventory[0] = fixture.BackpackInventory[0];
        fixture.BackpackInventory[1] = target[0];
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories).Returns([]);

        Assert.True(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, target, out var slots));

        Assert.Single(slots, slot => ReferenceEquals(slot, fixture.BackpackInventory[0]));
        Assert.DoesNotContain(target[0], slots);
        Assert.Equal(18, slots.Count);
    }

    /// <summary>Verifies each required player inventory must exist.</summary>
    [Theory]
    [InlineData(GlobalConstants.backpackInvClassName)]
    [InlineData(GlobalConstants.hotBarInvClassName)]
    public void TryCollect_MissingOwnInventory_ReturnsFalse(string inventoryClass)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        fixture.InventoryManagerMock.Setup(m => m.GetOwnInventory(inventoryClass)).Returns((IInventory)null!);

        Assert.False(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, CreateInventory(fixture, 9), out _));
    }

    /// <summary>Verifies the target is excluded even when it is a required player inventory.</summary>
    [Fact]
    public void TryCollect_TargetOwnInventory_ExcludesItsSlots()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories).Returns([fixture.BackpackInventory]);

        Assert.True(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, fixture.BackpackInventory, out var slots));
        Assert.Equal(fixture.HotbarInventory, slots);
    }

    /// <summary>Verifies closing a container removes it from the next discovery without mutating an earlier snapshot.</summary>
    [Fact]
    public void TryCollect_ContainerClosed_RefreshesPool()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var accessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(accessor.Object);
        InventoryGeneric target = CreateInventory(fixture, 9);
        InventoryGeneric chest = CreateInventory(fixture, 1);
        var owner = new Mock<BlockEntityGenericContainer>();
        owner.SetupGet(o => o.Inventory).Returns(chest);
        accessor.Setup(a => a.GetBlockEntity(chest.Pos)).Returns(owner.Object);
        List<IInventory> opened = [chest];
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories).Returns(opened);

        Assert.True(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, target, out var before));
        opened.Clear();
        Assert.True(AlloyTransferInventoryPolicy.TryCollect(fixture.World, fixture.Player, target, out var after));

        Assert.Contains(chest[0], before);
        Assert.DoesNotContain(chest[0], after);
    }

    #endregion

    #region Directional Permissions

    /// <summary>Verifies source slots cannot also be their own return destination.</summary>
    [Fact]
    public void CanReturnTo_SameSlot_IsRejected()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        fixture.WithBackpackSlot(0, fixture.CreateNonLightSource(1));
        Assert.False(AlloyTransferInventoryPolicy.CanReturnTo(fixture.BackpackInventory[0], fixture.BackpackInventory[0]));
    }

    /// <summary>Verifies permissions independently in both transfer directions.</summary>
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public void DirectionalPermissions_RequireSourceWithdrawalAndDestinationAcceptance(bool take, bool receive, bool hold, bool expected)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var source = new Mock<ItemSlot>((InventoryBase)null!);
        source.Object.Itemstack = new ItemStack(fixture.CreateNonLightSource(1));
        var destination = new Mock<ItemSlot>((InventoryBase)null!);
        source.Setup(s => s.CanTake()).Returns(take);
        destination.Setup(s => s.CanTakeFrom(source.Object, EnumMergePriority.AutoMerge)).Returns(receive);
        destination.Setup(s => s.CanHold(source.Object)).Returns(hold);

        Assert.Equal(take, AlloyTransferInventoryPolicy.CanWithdraw(source.Object));
        Assert.Equal(expected, AlloyTransferInventoryPolicy.CanReturnTo(destination.Object, source.Object));
    }

    /// <summary>Verifies native take and put locks apply independently.</summary>
    [Fact]
    public void DirectionalPermissions_NativeInventoryLocks_AreRespected()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        fixture.WithBackpackSlot(0, fixture.CreateNonLightSource(1));
        ItemSlot source = fixture.BackpackInventory[0];
        ItemSlot destination = fixture.HotbarInventory[0];
        fixture.BackpackInventory.TakeLocked = true;
        Assert.False(AlloyTransferInventoryPolicy.CanWithdraw(source));
        Assert.False(AlloyTransferInventoryPolicy.CanReturnTo(destination, source));
        fixture.BackpackInventory.TakeLocked = false;
        fixture.HotbarInventory.PutLocked = true;
        Assert.True(AlloyTransferInventoryPolicy.CanWithdraw(source));
        Assert.False(AlloyTransferInventoryPolicy.CanReturnTo(destination, source));
    }

    #endregion

    #endregion

    #region Private

    /// <summary>Creates a positioned storage inventory without initializing a live block entity.</summary>
    private static InventoryGeneric CreateInventory(VsTestFixture fixture, int coordinate)
    {
        return new InventoryGeneric(2, "chest", coordinate.ToString(), null!)
        {
            Api = fixture.Api,
            Pos = new BlockPos(coordinate, 0, 0)
        };
    }

    #endregion
}
