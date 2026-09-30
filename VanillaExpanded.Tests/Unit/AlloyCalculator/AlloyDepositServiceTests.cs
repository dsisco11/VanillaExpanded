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
    /// <summary>Verifies that planning rejects a firepit inventory that is not open.</summary>
    [Fact]
    public void CreatePlan_InventoryClosed_ReturnsInventoryClosed()
    {
        TestContext context = CreateContext();
        context.Fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([]);

        AlloyDepositResultCode result = AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            [context.Ingredient],
            new Dictionary<int, ItemStack> { [0] = new(context.Ingot, 5) },
            context.Fixture.Player,
            out AlloyDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InventoryClosed, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning rejects missing desired ingredient amounts.</summary>
    [Fact]
    public void CreatePlan_MissingCalculatedStack_ReturnsInvalidRequest()
    {
        TestContext context = CreateContext();

        AlloyDepositResultCode result = AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            [context.Ingredient],
            new Dictionary<int, ItemStack>(),
            context.Fixture.Player,
            out AlloyDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InvalidRequest, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning rejects a desired state unavailable across player and cooking slots.</summary>
    [Fact]
    public void CreatePlan_IngredientUnavailable_ReturnsInsufficientItems()
    {
        TestContext context = CreateContext();

        AlloyDepositResultCode result = AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            [context.Ingredient],
            new Dictionary<int, ItemStack> { [0] = new(context.Ingot, 5) },
            context.Fixture.Player,
            out AlloyDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.InsufficientItems, result);
        Assert.Null(plan);
    }

    /// <summary>Verifies that planning returns immutable desired slot targets when ingredients are available.</summary>
    [Fact]
    public void CreatePlan_IngredientAvailable_ReturnsPlan()
    {
        TestContext context = CreateContext(backpackAmount: 5);

        AlloyDepositResultCode result = AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            [context.Ingredient],
            new Dictionary<int, ItemStack> { [0] = new(context.Ingot, 5) },
            context.Fixture.Player,
            out AlloyDepositPlan? plan);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        AlloyDepositPlan createdPlan = Assert.IsType<AlloyDepositPlan>(plan);
        Assert.Equal([0, 1, 2, 3], createdPlan.Targets.Select(static target => target.SlotIndex));
        Assert.Equal([2, 1, 1, 1], createdPlan.Targets.Select(static target => target.Amount));
        Assert.Equal(5, createdPlan.Targets.Sum(static target => target.Amount));
    }

    /// <summary>Verifies that execution rejects targets outside the crucible cooking-slot range.</summary>
    [Fact]
    public void ExecutePlan_InvalidTargetSlot_ReturnsInvalidRequest()
    {
        TestContext context = CreateContext(backpackAmount: 5);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(99, context.Ingredient, 5)]);

        AlloyDepositResultCode result = AlloyDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.InvalidRequest, result);
    }

    /// <summary>Verifies that execution rejects a plan after the crucible inventory closes.</summary>
    [Fact]
    public void ExecutePlan_InventoryClosed_ReturnsInventoryClosed()
    {
        TestContext context = CreateContext(backpackAmount: 5);
        context.Fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([]);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, context.Ingredient, 5)]);

        AlloyDepositResultCode result = AlloyDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.InventoryClosed, result);
    }

    /// <summary>Verifies that execution rejects a plan when required ingredients are no longer available.</summary>
    [Fact]
    public void ExecutePlan_IngredientBecameUnavailable_ReturnsInsufficientItems()
    {
        TestContext context = CreateContext(backpackAmount: 4);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, context.Ingredient, 5)]);

        AlloyDepositResultCode result = AlloyDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.InsufficientItems, result);
    }

    /// <summary>Verifies that execution evacuates a different ingredient before inserting the desired one.</summary>
    [Fact]
    public void ExecutePlan_DifferentIngredient_ReplacesStack()
    {
        TestContext context = CreateContext(backpackAmount: 5);
        MockItem ironIngot = CreateItem(context.Fixture, 20, "ingot-iron");
        MockItem ironBit = CreateItem(context.Fixture, 21, "metalbit-iron");
        ironBit.CombustibleProps = new CombustibleProperties
        {
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack
            {
                Code = ironIngot.Code,
                ResolvedItemstack = new ItemStack(ironIngot)
            }
        };
        context.Inventory.CookingSlots[0].Itemstack = new ItemStack(ironBit, 3);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, context.Ingredient, 5)]);

        AlloyDepositResultCode result = AlloyDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal("metalbit-copper", context.Inventory.CookingSlots[0].Itemstack!.Collectible.Code.Path);
        Assert.Equal(5, context.Inventory.CookingSlots[0].StackSize);
        Assert.Contains(context.Fixture.BackpackInventory, slot => slot.Itemstack?.Collectible.Code == ironBit.Code);
    }

    /// <summary>Verifies that execution adds only the missing quantity to a matching cooking slot.</summary>
    [Fact]
    public void ExecutePlan_MatchingDeficit_AddsOnlyDeficit()
    {
        TestContext context = CreateContext(cookingAmount: 2, backpackAmount: 6);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, context.Ingredient, 5)]);

        AlloyDepositResultCode result = AlloyDepositService.ExecutePlan(
            context.Fixture.ClientApi,
            context.Firepit,
            plan,
            context.Fixture.Player);

        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(5, context.Inventory.CookingSlots[0].StackSize);
        Assert.Equal(3, context.Fixture.BackpackInventory[0].StackSize);
    }

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

    /// <summary>Creates a client firepit context containing one pure-metal ingredient.</summary>
    private static TestContext CreateContext(int cookingAmount = 0, int backpackAmount = 0)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(world => world.BlockAccessor).Returns(blockAccessor.Object);
        MockItem ingot = CreateItem(fixture, 10, "ingot-copper");
        MockItem metalBit = CreateItem(fixture, 11, "metalbit-copper");
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
        MockItem crucible = CreateItem(fixture, 12, "crucible");
        crucible.Attributes = JsonObject.FromJson("{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}");
        inventory[1].Itemstack = new ItemStack(crucible);
        if (cookingAmount > 0) inventory.CookingSlots[0].Itemstack = new ItemStack(metalBit, cookingAmount);
        if (backpackAmount > 0) fixture.BackpackInventory[0].Itemstack = new ItemStack(metalBit, backpackAmount);
        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);
        var ingredient = new MetalDepositIngredient(ingot.Code, new ItemStack(ingot), 1, 1);
        return new TestContext(fixture, firepit, inventory, ingot, ingredient);
    }

    private sealed record TestContext(
        VsTestFixture Fixture,
        BlockEntityFirepit Firepit,
        InventorySmelting Inventory,
        MockItem Ingot,
        MetalDepositIngredient Ingredient);
}