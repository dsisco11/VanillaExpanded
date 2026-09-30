using Moq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies client-side execution of planned fuel-slot corrections.</summary>
[Trait("Category", "Unit")]
public class AlloyFuelDepositServiceTests
{
    /// <summary>Verifies that planning requires smeltable input in the firepit.</summary>
    [Fact]
    public void CreatePlan_NoInput_ReturnsInvalidRequest()
    {
        TestContext context = CreateContext(includeInput: false);

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            context.Fixture.Player,
            out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InvalidRequest, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning reports unavailable suitable fuel.</summary>
    [Fact]
    public void CreatePlan_NoSuitableFuel_ReturnsInsufficientItems()
    {
        TestContext context = CreateContext();

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            context.Fixture.Player,
            out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InsufficientItems, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning selects available fuel and records the expected current slot state.</summary>
    [Fact]
    public void CreatePlan_SuitableFuelAvailable_ReturnsPlan()
    {
        TestContext context = CreateContext(backpackFuelAmount: 64);

        AlloyDepositResultCode result = AlloyFuelDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            context.Fixture.Player,
            out AlloyFuelDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        AlloyFuelDepositPlan createdPlan = Assert.IsType<AlloyFuelDepositPlan>(plan);
        Assert.Equal(context.Fuel.Code, createdPlan.DesiredStack.Collectible.Code);
        Assert.InRange(createdPlan.DesiredAmount, 1, 64);
        Assert.Null(createdPlan.ExpectedCurrentStack);
        Assert.Equal(0, createdPlan.ExpectedCurrentAmount);
    }

    /// <summary>Verifies that execution rejects a plan when the fuel slot changed after planning.</summary>
    [Fact]
    public void ExecutePlan_StaleCurrentState_ReturnsInvalidRequest()
    {
        TestContext context = CreateContext(currentFuelAmount: 4);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(context.Fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(context.Fuel, 3),
            ExpectedCurrentAmount: 3);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.InvalidRequest, result);
        Assert.Equal(4, context.Firepit.fuelSlot.StackSize);
    }

    /// <summary>Verifies that execution adds only a matching fuel deficit.</summary>
    [Fact]
    public void ExecutePlan_MatchingDeficit_AddsOnlyDeficit()
    {
        TestContext context = CreateContext(currentFuelAmount: 2, backpackFuelAmount: 8);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(context.Fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(context.Fuel, 2),
            ExpectedCurrentAmount: 2);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(5, context.Firepit.fuelSlot.StackSize);
        Assert.Equal(5, context.Fixture.BackpackInventory[0].StackSize);
    }

    /// <summary>Verifies that execution replaces a different fuel stack with the desired fuel.</summary>
    [Fact]
    public void ExecutePlan_DifferentFuel_ReplacesStack()
    {
        TestContext context = CreateContext(currentFuelAmount: 4, backpackFuelAmount: 8);
        MockItem otherFuel = CreateFuel(context.Fixture, 2, "other-fuel");
        context.Firepit.fuelSlot.Itemstack = new ItemStack(otherFuel, 4);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(context.Fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(otherFuel, 4),
            ExpectedCurrentAmount: 4);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(context.Fuel.Code, context.Firepit.fuelStack.Collectible.Code);
        Assert.Equal(5, context.Firepit.fuelSlot.StackSize);
        Assert.Contains(context.Fixture.BackpackInventory, slot => slot.Itemstack?.Collectible.Code == otherFuel.Code);
    }

    /// <summary>Regression test ensuring excess matching fuel is reduced rather than fully evacuated.</summary>
    [Fact]
    public void ExecutePlan_ExcessMatchingFuel_RemovesOnlyExcess()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem fuel = fixture.CreateNonLightSource(1);
        fuel.Code = new AssetLocation("game", "fuel");
        fuel.MaxStackSize = 64;
        var firepit = new BlockEntityFirepit
        {
            Api = fixture.Api,
            Pos = new BlockPos(0)
        };
        var inventory = Assert.IsType<InventorySmelting>(firepit.Inventory);
        inventory.Api = fixture.Api;
        inventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        firepit.fuelSlot.Itemstack = new ItemStack(fuel, 8);
        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);
        var plan = new AlloyFuelDepositPlan(
            new ItemStack(fuel),
            DesiredAmount: 5,
            ExpectedCurrentStack: new ItemStack(fuel, 8),
            ExpectedCurrentAmount: 8);

        AlloyDepositResultCode result = AlloyFuelDepositService.ExecutePlan(
            fixture.ClientApi,
            firepit,
            plan,
            fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(5, firepit.fuelSlot.StackSize);
        Assert.Equal(3, fixture.BackpackInventory[0].StackSize);
        fixture.ClientNetworkMock!.Verify(
            network => network.SendPacketClient(It.IsAny<object>()),
            Times.Once);
    }

    /// <summary>Creates a deterministic firepit and fuel inventory context.</summary>
    private static TestContext CreateContext(
        bool includeInput = true,
        int currentFuelAmount = 0,
        int backpackFuelAmount = 0)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(blockAccessor.Object);
        MockItem fuel = CreateFuel(fixture, 1, "fuel");
        var firepit = new BlockEntityFirepit
        {
            Api = fixture.Api,
            Pos = new BlockPos(0)
        };
        var inventory = Assert.IsType<InventorySmelting>(firepit.Inventory);
        inventory.Api = fixture.Api;
        inventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        if (includeInput)
        {
            var crucible = new TestSmeltingContainer
            {
                Code = new AssetLocation("game", "crucible"),
                Attributes = Vintagestory.API.Datastructures.JsonObject.FromJson(
                    "{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}")
            };
            inventory[1].Itemstack = new ItemStack(crucible);
        }
        if (currentFuelAmount > 0) firepit.fuelSlot.Itemstack = new ItemStack(fuel, currentFuelAmount);
        if (backpackFuelAmount > 0) fixture.BackpackInventory[0].Itemstack = new ItemStack(fuel, backpackFuelAmount);
        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);
        return new TestContext(fixture, firepit, fuel);
    }

    /// <summary>Creates a suitable stackable fuel item.</summary>
    private static MockItem CreateFuel(VsTestFixture fixture, int id, string path)
    {
        MockItem fuel = fixture.CreateNonLightSource(id);
        fuel.Code = new AssetLocation("game", path);
        fuel.MaxStackSize = 64;
        fuel.CombustibleProps = new CombustibleProperties
        {
            BurnDuration = 40,
            BurnTemperature = 1_300
        };
        return fuel;
    }

    private sealed record TestContext(
        VsTestFixture Fixture,
        BlockEntityFirepit Firepit,
        MockItem Fuel);

    /// <summary>Provides deterministic melting properties for fuel-plan tests.</summary>
    private sealed class TestSmeltingContainer : BlockSmeltingContainer
    {
        /// <summary>Returns a fixed melting point.</summary>
        public override float GetMeltingPoint(
            IWorldAccessor world,
            ISlotProvider cookingSlotsProvider,
            ItemSlot inputSlot) => 1_000;

        /// <summary>Returns a fixed melting duration.</summary>
        public override float GetMeltingDuration(
            IWorldAccessor world,
            ISlotProvider cookingSlotsProvider,
            ItemSlot inputSlot) => 30;
    }
}