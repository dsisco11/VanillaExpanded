using Moq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
public class AlloyDepositServiceTests
{
    [Fact]
    public void ResultCodes_HaveStableIntegerValues()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(AlloyDepositResultCode)));
        Assert.Equal(0, (int)AlloyDepositResultCode.Success);
        Assert.Equal(1, (int)AlloyDepositResultCode.InvalidRequest);
        Assert.Equal(2, (int)AlloyDepositResultCode.InventoryClosed);
        Assert.Equal(3, (int)AlloyDepositResultCode.InvalidRecipe);
        Assert.Equal(4, (int)AlloyDepositResultCode.InsufficientItems);
        Assert.Equal(5, (int)AlloyDepositResultCode.InsufficientSpace);
        Assert.Equal(6, (int)AlloyDepositResultCode.TransferFailed);
    }

    [Fact]
    public void Execute_ValidRequest_DepositsCompleteAlloy()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 1);

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.True(context.Fixture.BackpackInventory[0].Empty);
        Assert.True(context.Fixture.BackpackInventory[1].Empty);
        Assert.Equal([3, 3, 3, 1], context.Inventory.CookingSlots.Select(static slot => slot.StackSize));
        Assert.True(context.Recipe.Matches(context.Inventory.CookingSlots.Select(static slot => slot.Itemstack).ToArray()));
    }

    [Fact]
    public void Execute_ReturningCookingStack_PrefersMatchingPlayerStack()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 1);
        context.Fixture.WithBackpackSlot(2, context.CopperSource, 5);
        context.Inventory.CookingSlots[0].Itemstack = new ItemStack(context.CopperSource, 2);

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.Success, result);
        ItemSlot firstReturnTarget = context.Fixture.InventoryManagerMock.Invocations
            .Select(static invocation => invocation.Arguments)
            .Where(arguments => arguments.Count >= 2)
            .Where(arguments => arguments[0] is ItemSlot source && source.Inventory == context.Inventory)
            .Select(arguments => Assert.IsAssignableFrom<ItemSlot>(arguments[1]))
            .First();
        Assert.Same(context.Fixture.BackpackInventory, firstReturnTarget.Inventory);
        Assert.Equal(context.CopperSource.Code, firstReturnTarget.Itemstack?.Collectible.Code);
        Assert.Equal(7, firstReturnTarget.StackSize);
    }

    [Fact]
    public void Execute_TakingIngredients_UsesSmallestMatchingStackFirst()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 12, tinCount: 1);
        context.Fixture.WithBackpackSlot(2, context.CopperSource, 3);

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(6, context.Fixture.BackpackInventory[0].StackSize);
        Assert.True(context.Fixture.BackpackInventory[2].Empty);
    }

    [Fact]
    public void Execute_IngredientMissing_RestoresAllSlots()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 0);
        context.Inventory.CookingSlots[0].Itemstack = new ItemStack(context.CopperSource, 2);
        ItemStack?[] beforePlayer = context.Fixture.BackpackInventory
            .Select(static slot => slot.Itemstack?.Clone())
            .ToArray();
        ItemStack?[] beforeCooking = context.Inventory.CookingSlots
            .Select(static slot => slot.Itemstack?.Clone())
            .ToArray();

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, result);
        AssertStacksEqual(beforePlayer, context.Fixture.BackpackInventory.Select(static slot => slot.Itemstack).ToArray());
        AssertStacksEqual(beforeCooking, context.Inventory.CookingSlots.Select(static slot => slot.Itemstack).ToArray());
    }

    [Fact]
    public void Execute_TransferReplacesPlayerSlot_RollbackUsesCurrentInventorySlot()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 0);
        context.Inventory.CookingSlots[0].Itemstack = new ItemStack(context.CopperSource, 2);
        ItemStack?[] beforePlayer = context.Fixture.BackpackInventory
            .Select(static slot => slot.Itemstack?.Clone())
            .ToArray();
        ItemStack?[] beforeCooking = context.Inventory.CookingSlots
            .Select(static slot => slot.Itemstack?.Clone())
            .ToArray();
        bool replacedSlot = false;

        context.Fixture.InventoryManagerMock
            .Setup(manager => manager.TryTransferTo(
                It.IsAny<ItemSlot>(),
                It.IsAny<ItemSlot>(),
                ref It.Ref<ItemStackMoveOperation>.IsAny))
            .Returns((ItemSlot source, ItemSlot target, ref ItemStackMoveOperation operation) =>
            {
                if (!replacedSlot && target.Inventory == context.Fixture.BackpackInventory)
                {
                    int targetIndex = target.Inventory.GetSlotId(target);
                    var replacement = new ItemSlotSurvival(context.Fixture.BackpackInventory)
                    {
                        Itemstack = target.Itemstack
                    };
                    context.Fixture.BackpackInventory[targetIndex] = replacement;
                    target = replacement;
                    replacedSlot = true;
                }

                if (source.Empty || (!target.Empty && target.Itemstack?.Collectible.Code != source.Itemstack?.Collectible.Code))
                {
                    operation.MovedQuantity = 0;
                    return null;
                }

                int moved = Math.Min(operation.RequestedQuantity, source.StackSize);
                if (target.Empty)
                {
                    target.Itemstack = source.TakeOut(moved);
                }
                else
                {
                    target.Itemstack!.StackSize += moved;
                    source.TakeOut(moved);
                }

                operation.MovedQuantity = moved;
                return new object();
            });

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.True(replacedSlot);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, result);
        AssertStacksEqual(beforePlayer, context.Fixture.BackpackInventory.Select(static slot => slot.Itemstack).ToArray());
        AssertStacksEqual(beforeCooking, context.Inventory.CookingSlots.Select(static slot => slot.Itemstack).ToArray());
    }

    [Fact]
    public void Execute_InvalidRatio_DoesNotMutateInventory()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 1);
        context.Request.SlotAmounts = [2, 2, 1, 5];
        ItemStack?[] beforePlayer = context.Fixture.BackpackInventory
            .Select(static slot => slot.Itemstack?.Clone())
            .ToArray();

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.InvalidRecipe, result);
        AssertStacksEqual(beforePlayer, context.Fixture.BackpackInventory.Select(static slot => slot.Itemstack).ToArray());
        Assert.All(context.Inventory.CookingSlots, static slot => Assert.True(slot.Empty));
    }

    [Fact]
    public void Execute_InventoryNotOpen_ReturnsInventoryClosed()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 1);
        context.Fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([]);

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.InventoryClosed, result);
    }

    [Fact]
    public void Execute_DuplicateSlotIndex_ReturnsInvalidRecipe()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 9, tinCount: 1);
        context.Request.SlotIndices = [0, 0, 2, 3];

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            [context.Recipe]);

        // Assert
        Assert.Equal(AlloyDepositResultCode.InvalidRecipe, result);
    }

    [Fact]
    public void Execute_PureMetalWithoutRegisteredRecipe_DepositsSuccessfully()
    {
        // Arrange
        TestContext context = CreateContext(copperCount: 4, tinCount: 0);
        string copperCode = context.Recipe.Ingredients[0].Code.ToString();
        context.Request.AlloyCode = copperCode;
        context.Request.SlotIndices = [0];
        context.Request.SlotIngredientCodes = [copperCode];
        context.Request.SlotAmounts = [4];
        context.Fixture.WorldMock
            .Setup(world => world.GetItem(It.Is<AssetLocation>(code => code.ToString() == copperCode)))
            .Returns(context.Recipe.Ingredients[0].ResolvedItemstack!.Item);

        // Act
        AlloyDepositResultCode result = AlloyDepositService.Execute(
            context.Fixture.World,
            context.Fixture.Player,
            context.Firepit,
            context.Request,
            []);

        // Assert
        Assert.Equal(AlloyDepositResultCode.Success, result);
        Assert.Equal(4, context.Inventory.CookingSlots[0].StackSize);
        Assert.All(context.Inventory.CookingSlots.Skip(1), static slot => Assert.True(slot.Empty));
    }

    private static TestContext CreateContext(int copperCount, int tinCount)
    {
        var fixture = VsTestFixture.Server();
        var blockAccessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.Setup(world => world.BlockAccessor).Returns(blockAccessor.Object);

        MockItem copperIngot = CreateItem(10, "ingot-copper", fixture.Api);
        MockItem tinIngot = CreateItem(11, "ingot-tin", fixture.Api);
        MockItem outputIngot = CreateItem(12, "ingot-bronze", fixture.Api);
        MockItem copperSource = CreateSmeltable(20, "metalbit-copper", copperIngot, fixture.Api);
        MockItem tinSource = CreateSmeltable(21, "metalbit-tin", tinIngot, fixture.Api);

        if (copperCount > 0) fixture.WithBackpackSlot(0, copperSource, copperCount);
        if (tinCount > 0) fixture.WithBackpackSlot(1, tinSource, tinCount);

        var firepit = new BlockEntityFirepit
        {
            Api = fixture.Api,
            Pos = new BlockPos(0)
        };
        var inventory = Assert.IsType<InventorySmelting>(firepit.Inventory);
        inventory.Api = fixture.Api;
        inventory.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;

        MockItem crucible = CreateItem(30, "crucible", fixture.Api);
        crucible.Attributes = JsonObject.FromJson("{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}");
        inventory[1].Itemstack = new ItemStack(crucible);
        foreach (ItemSlot slot in inventory.CookingSlots)
        {
            slot.MaxSlotStackSize = 64;
        }

        fixture.InventoryManagerMock
            .SetupGet(manager => manager.OpenedInventories)
            .Returns([inventory]);

        var recipe = new AlloyRecipe
        {
            Enabled = true,
            Output = new JsonItemStack
            {
                Code = outputIngot.Code,
                ResolvedItemstack = new ItemStack(outputIngot)
            },
            Ingredients =
            [
                new MetalAlloyIngredient
                {
                    Code = copperIngot.Code,
                    MinRatio = 0.88f,
                    MaxRatio = 0.92f,
                    ResolvedItemstack = new ItemStack(copperIngot)
                },
                new MetalAlloyIngredient
                {
                    Code = tinIngot.Code,
                    MinRatio = 0.08f,
                    MaxRatio = 0.12f,
                    ResolvedItemstack = new ItemStack(tinIngot)
                }
            ]
        };

        var request = new Packet_RequestAlloyDeposit
        {
            RequestId = "request-1",
            Position = firepit.Pos.Copy(),
            AlloyCode = outputIngot.Code.ToString(),
            SlotIndices = [0, 1, 2, 3],
            SlotIngredientCodes =
            [
                copperIngot.Code.ToString(),
                copperIngot.Code.ToString(),
                copperIngot.Code.ToString(),
                tinIngot.Code.ToString()
            ],
            SlotAmounts = [3, 3, 3, 1]
        };

        return new TestContext(fixture, firepit, inventory, recipe, request, copperSource);
    }

    private static MockItem CreateItem(int id, string path, ICoreAPI api)
    {
        var item = MockItem.CreateNonLightSource(id, api);
        item.Code = new AssetLocation("game", path);
        item.MaxStackSize = 64;
        return item;
    }

    private static MockItem CreateSmeltable(int id, string path, MockItem ingot, ICoreAPI api)
    {
        MockItem item = CreateItem(id, path, api);
        item.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 1000,
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack
            {
                Code = ingot.Code,
                ResolvedItemstack = new ItemStack(ingot)
            }
        };
        return item;
    }

    private static void AssertStacksEqual(IReadOnlyList<ItemStack?> expected, IReadOnlyList<ItemStack?> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index]?.Collectible.Code, actual[index]?.Collectible.Code);
            Assert.Equal(expected[index]?.StackSize, actual[index]?.StackSize);
        }
    }

    private sealed record TestContext(
        VsTestFixture Fixture,
        BlockEntityFirepit Firepit,
        InventorySmelting Inventory,
        AlloyRecipe Recipe,
        Packet_RequestAlloyDeposit Request,
        MockItem CopperSource);
}
