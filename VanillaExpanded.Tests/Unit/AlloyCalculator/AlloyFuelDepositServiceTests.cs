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
}