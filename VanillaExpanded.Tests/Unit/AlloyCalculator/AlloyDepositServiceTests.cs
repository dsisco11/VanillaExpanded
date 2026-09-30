using Moq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies client-side execution of planned ingredient-slot corrections.</summary>
[Trait("Category", "Unit")]
public class AlloyDepositServiceTests
{
    /// <summary>Verifies that a matching cooking-slot excess is returned without rebuilding the slot.</summary>
    [Fact]
    public void ExecutePlan_ExcessMatchingIngredient_RemovesOnlyExcess()
    {
        VsTestFixture fixture = VsTestFixture.Client();
        MockItem ingot = CreateItem(fixture, 1, "ingot-copper");
        MockItem metalBit = CreateItem(fixture, 2, "metalbit-copper");
        metalBit.CombustibleProps = new CombustibleProperties
        {
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack
            {
                Code = ingot.Code,
                ResolvedItemstack = new ItemStack(ingot)
            }
        };
        var firepit = new BlockEntityFirepit
        {
            Api = fixture.Api,
            Pos = new BlockPos(0)
        };
        var inventory = Assert.IsType<InventorySmelting>(firepit.Inventory);
        inventory.Api = fixture.Api;
        inventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        MockItem crucible = CreateItem(fixture, 3, "crucible");
        crucible.Attributes = JsonObject.FromJson("{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}");
        inventory[1].Itemstack = new ItemStack(crucible);
        inventory.CookingSlots[0].Itemstack = new ItemStack(metalBit, 8);
        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);
        var ingredient = new MetalDepositIngredient(ingot.Code, new ItemStack(ingot), 1, 1);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, ingredient, 5)]);

        AlloyDepositResultCode result = AlloyDepositService.ExecutePlan(
            fixture.ClientApi,
            firepit,
            plan,
            fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(5, inventory.CookingSlots[0].StackSize);
        Assert.Equal(3, fixture.BackpackInventory[0].StackSize);
        fixture.ClientNetworkMock!.Verify(
            network => network.SendPacketClient(It.IsAny<object>()),
            Times.Once);
    }

    /// <summary>Creates a stackable test item with a stable game asset code.</summary>
    private static MockItem CreateItem(VsTestFixture fixture, int id, string path)
    {
        MockItem item = fixture.CreateNonLightSource(id);
        item.Code = new AssetLocation("game", path);
        item.MaxStackSize = 64;
        return item;
    }
}