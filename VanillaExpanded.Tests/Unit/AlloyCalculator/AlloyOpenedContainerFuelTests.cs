using Moq;
using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies fuel planning and correction across live opened storage inventories.</summary>
[Trait("Category", "Unit")]
public class AlloyOpenedContainerFuelTests
{
    #region Public API

    #region Supply And Planning

    /// <summary>Verifies container-only and mixed supplies deposit the calculated quantity without losing fuel.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void OpenedFuel_CreateAndExecutePlan_ConservesSupply(int playerAmount)
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        int totalSupply = 64;
        if (playerAmount > 0)
        {
            // Split the exact requirement so neither player storage nor the chest can satisfy it alone.
            Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
                context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var baseline));
            totalSupply = baseline!.DesiredAmount;
            Assert.True(totalSupply > playerAmount);
            context.Chest[0].Itemstack = new ItemStack(context.Fuel, totalSupply - playerAmount);
            context.Fixture.BackpackInventory[0].Itemstack = new ItemStack(context.Fuel, playerAmount);
        }
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var plan));
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi, context.Firepit, plan!, context.Fixture.Player));
        Assert.Equal(plan!.DesiredAmount, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(totalSupply, context.Firepit.fuelSlot.StackSize + context.Chest.Sum(s => s.StackSize)
            + context.Fixture.BackpackInventory.Sum(s => s.StackSize));
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.AtLeastOnce);
    }

    /// <summary>Verifies queued fuel contributes once, even when the target appears repeatedly in opened inventories.</summary>
    [Fact]
    public void QueuedAndExternalFuel_CountOnceAndCombine()
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var baseline));
        int required = baseline!.DesiredAmount;
        Assert.True(required > 1);
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, required - 1);
        context.Firepit.fuelSlot.Itemstack = new ItemStack(context.Fuel, 1);
        context.Opened.Add(context.Target);
        context.Opened.Add(context.Chest);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var plan));
        Assert.Equal(required, plan!.DesiredAmount);
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, required - 2);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out _));
    }

    /// <summary>Verifies closed, take-locked, and too-cold storage supplies cannot produce an available fuel plan.</summary>
    [Theory]
    [InlineData("closed")]
    [InlineData("locked")]
    [InlineData("cold")]
    [InlineData("nonburning")]
    public void UnavailableStorageFuel_RejectsPlan(string restriction)
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        if (restriction == "closed") context.Opened.Remove(context.Chest);
        if (restriction == "locked") context.Chest.TakeLocked = true;
        if (restriction == "cold") context.Fuel.CombustibleProps.BurnTemperature = 900;
        if (restriction == "nonburning") context.Fuel.CombustibleProps.BurnDuration = 0;
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var plan));
        Assert.Null(plan);
    }

    /// <summary>Verifies selection minimizes required fuel across player and container candidates.</summary>
    [Fact]
    public void ContainerLongBurnFuel_SelectsMinimumQuantity()
    {
        Context context = CreateContext();
        context.Fixture.BackpackInventory[0].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var baseline));
        MockItem efficient = CreateFuel(context.Fixture, 2, "long-burn-fuel");
        efficient.CombustibleProps.BurnDuration = 400;
        context.Chest[0].Itemstack = new ItemStack(efficient, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var selected));
        Assert.Equal(efficient.Code, selected!.DesiredStack.Collectible.Code);
        Assert.True(selected.DesiredAmount < baseline!.DesiredAmount);
    }

    /// <summary>Verifies container candidates obey effective heat and burn duration modifiers.</summary>
    [Fact]
    public void ContainerFuel_FirepitModifiersAffectEligibilityAndQuantity()
    {
        Context context = CreateContext();
        context.Fuel.CombustibleProps.BurnTemperature = 900;
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out _));
        context.Firepit.TestHeatModifier = 1.5f;
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var normal));
        context.Firepit.TestBurnDurationModifier = 10;
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var extended));
        Assert.True(extended!.DesiredAmount < normal!.DesiredAmount);
    }

    /// <summary>Verifies abundant container fuel cannot exceed the selected fuel's stack size.</summary>
    [Fact]
    public void ContainerFuel_RequiredQuantityOverStackLimitRejectsPlan()
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var baseline));
        Assert.True(baseline!.DesiredAmount > 1);
        context.Fuel.MaxStackSize = baseline.DesiredAmount - 1;
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out _));
    }

    #endregion

    #region Correction And Lifecycle

    /// <summary>Verifies replacement and excess fuel return to storage when player slots have no capacity.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullPlayerInventory_ReturnsFuelCorrectionsToStorage(bool replace)
    {
        Context context = CreateContext();
        MockItem other = CreateFuel(context.Fixture, 2, "other-fuel");
        // Fill player inventories with incompatible full stacks so only opened storage can accept a return.
        foreach (ItemSlot slot in context.Fixture.BackpackInventory.Concat(context.Fixture.HotbarInventory))
            slot.Itemstack = new ItemStack(other, 64);
        context.Firepit.fuelSlot.Itemstack = new ItemStack(replace ? other : context.Fuel, 8);
        if (replace) context.Chest[0].Itemstack = new ItemStack(context.Fuel, 5);
        var plan = new AlloyFuelDepositPlan(new ItemStack(context.Fuel), 5,
            context.Firepit.fuelStack.Clone(), 8);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi, context.Firepit, plan, context.Fixture.Player));
        Assert.Equal(5, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(context.Fuel.Code, context.Firepit.fuelStack.Collectible.Code);
        Assert.Equal(replace ? 8 : 3, context.Chest.Sum(s => s.StackSize));
        Assert.All(context.Fixture.BackpackInventory.Concat(context.Fixture.HotbarInventory),
            slot => Assert.Equal(64, slot.StackSize));
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()),
            replace ? Times.Exactly(2) : Times.Once());
    }

    /// <summary>Verifies closing or locking a source after planning prevents stale withdrawals.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceUnavailableAfterPlanning_StopsWithdrawal(bool locked)
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var plan));
        if (locked) context.Chest.TakeLocked = true;
        else context.Opened.Remove(context.Chest);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi, context.Firepit, plan!, context.Fixture.Player));
        Assert.True(context.Firepit.fuelSlot.Empty);
        Assert.Equal(64, context.Chest[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    /// <summary>Verifies closure during correction preserves the first transfer and stops further movements.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InventoryClosesDuringPacket_ConservesPartialMovement(bool targetClosed)
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 2);
        context.Chest[1].Itemstack = new ItemStack(context.Fuel, 3);
        var plan = new AlloyFuelDepositPlan(new ItemStack(context.Fuel), 5, null, 0);
        context.Fixture.ClientNetworkMock!.Setup(n => n.SendPacketClient(It.IsAny<object>()))
            .Callback(() => context.Opened.Remove(targetClosed ? context.Target : context.Chest));
        Assert.Equal(targetClosed ? AlloyDepositResultCode.InventoryClosed : AlloyDepositResultCode.InsufficientItems,
            AlloyFuelDepositService.ExecutePlan(context.Fixture.ClientApi, context.Firepit, plan, context.Fixture.Player));
        Assert.Equal(2, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(3, context.Chest.Sum(s => s.StackSize));
        context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Once);
    }

    /// <summary>Verifies an unexpected queued-fuel change is rejected before container fuel is moved.</summary>
    [Fact]
    public void QueuedFuelChangedAfterPlan_RejectsWithoutTransfers()
    {
        Context context = CreateContext();
        context.Chest[0].Itemstack = new ItemStack(context.Fuel, 64);
        Assert.Equal(AlloyDepositResultCode.Success, AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, context.Fixture.Player, out var plan));
        context.Firepit.fuelSlot.Itemstack = new ItemStack(context.Fuel, 1);
        Assert.Equal(AlloyDepositResultCode.InvalidRequest, AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi, context.Firepit, plan!, context.Fixture.Player));
        Assert.Equal(64, context.Chest[0].StackSize);
        Assert.Equal(1, context.Firepit.fuelSlot.StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    #endregion

    #endregion

    #region Private

    /// <summary>Creates a stackable fuel candidate with stable identity and deterministic burn properties.</summary>
    private static MockItem CreateFuel(VsTestFixture fixture, int id, string path)
    {
        MockItem fuel = fixture.CreateNonLightSource(id);
        fuel.Code = new AssetLocation("game", path);
        fuel.MaxStackSize = 64;
        fuel.CombustibleProps = new CombustibleProperties { BurnDuration = 40, BurnTemperature = 1_300 };
        return fuel;
    }

    /// <summary>Creates an opened firepit and owner-backed chest for live storage policy evaluation.</summary>
    private static Context CreateContext()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var accessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(w => w.BlockAccessor).Returns(accessor.Object);
        MockItem fuel = CreateFuel(fixture, 1, "fuel");
        var firepit = new TestFirepit { Api = fixture.Api, Pos = new BlockPos(0) };
        var target = Assert.IsType<InventorySmelting>(firepit.Inventory);
        target.Api = fixture.Api;
        target.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        var crucible = new TestSmeltingContainer { Code = new AssetLocation("game", "crucible"),
            Attributes = JsonObject.FromJson("{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}") };
        target[1].Itemstack = new ItemStack(crucible);
        var chest = new InventoryGeneric(2, "chest", "opened-fuel", null!) {
            Api = fixture.Api, Pos = new BlockPos(1, 0, 0), InvNetworkUtil = fixture.InvNetworkUtilMock.Object };
        var owner = new Mock<BlockEntityGenericContainer>();
        owner.SetupGet(o => o.Inventory).Returns(chest);
        accessor.Setup(a => a.GetBlockEntity(chest.Pos)).Returns(owner.Object);
        List<IInventory> opened = [target, chest];
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories).Returns(opened);
        return new Context(fixture, firepit, target, chest, opened, fuel);
    }

    /// <summary>Owns mutable state for fuel planning and transfer lifecycle tests.</summary>
    private sealed record Context(VsTestFixture Fixture, TestFirepit Firepit, InventorySmelting Target,
        InventoryGeneric Chest, List<IInventory> Opened, MockItem Fuel);

    /// <summary>Exposes deterministic firepit modifiers for fuel candidate comparisons.</summary>
    private sealed class TestFirepit : BlockEntityFirepit
    {
        #region Public API
        public float TestHeatModifier { get; set; } = 1;
        public float TestBurnDurationModifier { get; set; } = 1;
        /// <summary>Returns the configured effective heat multiplier.</summary>
        public override float HeatModifier => TestHeatModifier;
        /// <summary>Returns the configured effective burn-duration multiplier.</summary>
        public override float BurnDurationModifier => TestBurnDurationModifier;
        #endregion
    }

    /// <summary>Provides deterministic melting properties for a valid fuel-planning input.</summary>
    private sealed class TestSmeltingContainer : BlockSmeltingContainer
    {
        #region Public API
        /// <summary>Returns the melting point for the test metal.</summary>
        public override float GetMeltingPoint(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot) => 1_000;
        /// <summary>Returns the fixed melting duration.</summary>
        public override float GetMeltingDuration(IWorldAccessor world, ISlotProvider cookingSlotsProvider, ItemSlot inputSlot) => 30;
        #endregion
    }

    #endregion
}
