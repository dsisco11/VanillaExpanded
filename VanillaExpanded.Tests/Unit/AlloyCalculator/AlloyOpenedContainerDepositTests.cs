using Moq;
using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>Verifies ingredient transfers against live opened storage and directional restrictions.</summary>
[Trait("Category", "Unit")]
public class AlloyOpenedContainerDepositTests
{
    #region Public API

    #region Supply And Planning

    [Theory]
    [InlineData("unchanged")]
    [InlineData("closed")]
    [InlineData("take-locked")]
    [InlineData("shortage")]
    [InlineData("too-many-forms")]
    public void ThreeMetalFallback_PlanAndExecute_ConservesItemsAndRevalidatesSupplies(string change)
    {
        Context context = CreateContext(15, 0);
        MockItem metalB = CreateItem(context.Fixture, 30, "ingot-metal-b");
        MockItem metalC = CreateItem(context.Fixture, 31, "ingot-metal-c");
        MockItem aNuggets = CreateForm(32, "nugget-metal-a", context.Ingot);
        MockItem aChunks = CreateForm(33, "chunk-metal-a", context.Ingot);
        MockItem bBits = CreateForm(34, "metalbit-metal-b", metalB);
        MockItem bNuggets = CreateForm(35, "nugget-metal-b", metalB);
        MockItem cBits = CreateForm(36, "metalbit-metal-c", metalC);
        context.Metal.CombustibleProps.SmeltedRatio = 20;
        context.Fixture.BackpackInventory[1].Itemstack = new ItemStack(aNuggets, 10);
        context.Fixture.BackpackInventory[2].Itemstack = new ItemStack(aChunks, 5);
        context.Fixture.BackpackInventory[3].Itemstack = new ItemStack(bBits, 3);
        context.Chest[0].Itemstack = new ItemStack(bNuggets, 4);
        context.Chest[1].Itemstack = new ItemStack(cBits, 10);
        var fuel = new ItemStack(CreateItem(context.Fixture, 37, "fuel"), 4);
        context.Target[0].Itemstack = fuel;
        MetalDepositIngredient[] ingredients =
        [
            context.Ingredient,
            new(metalB.Code, new ItemStack(metalB), 0, 1),
            new(metalC.Code, new ItemStack(metalC), 0, 1)
        ];
        var requested = new Dictionary<int, ItemStack>
        {
            [0] = new(aNuggets, 10), [1] = new(bBits, 7), [2] = new(cBits, 4)
        };
        ItemSlot[] external = [.. context.Fixture.BackpackInventory,
            .. context.Fixture.HotbarInventory, .. context.Chest];
        var beforePlanning = external.Select(slot => (Stack: slot.Itemstack, Amount: slot.StackSize)).ToArray();

        Assert.Equal(AlloyDepositResultCode.Success, AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, ingredients, requested,
            context.Fixture.Player, out AlloyDepositPlan? created));
        AlloyDepositPlan plan = Assert.IsType<AlloyDepositPlan>(created);
        Assert.Equal(4, plan.Targets.Length);
        Assert.Equal(4, plan.Targets.Select(target => target.SlotIndex).Distinct().Count());
        for (int index = 0; index < ingredients.Length; index++)
            Assert.Equal(requested[index].StackSize, plan.Targets
                .Where(target => target.Ingredient == ingredients[index]).Sum(target => target.Amount));
        Assert.All(plan.Targets, target =>
        {
            Assert.InRange(target.Amount, 1, Math.Min(
                context.Target.CookingSlots[target.SlotIndex].MaxSlotStackSize,
                target.DesiredStack!.Collectible.MaxStackSize));
            Assert.True(target.Amount <= external
                .Where(slot => slot.Itemstack?.Collectible == target.DesiredStack.Collectible)
                .Sum(slot => slot.StackSize));
        });
        foreach (var form in plan.Targets.GroupBy(target => target.DesiredStack!.Collectible.Code))
            Assert.True(form.Sum(target => target.Amount) <= external
                .Where(slot => slot.Itemstack?.Collectible.Code == form.Key).Sum(slot => slot.StackSize));
        for (int index = 0; index < external.Length; index++)
        {
            Assert.Same(beforePlanning[index].Stack, external[index].Itemstack);
            Assert.Equal(beforePlanning[index].Amount, external[index].StackSize);
        }
        Assert.All(context.Target.CookingSlots, slot => Assert.True(slot.Empty));
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);

        switch (change)
        {
            case "closed":
                context.Opened.Remove(context.Chest);
                break;
            case "take-locked":
                context.Chest.TakeLocked = true;
                break;
            case "shortage":
                context.Chest[1].Itemstack!.StackSize = 3;
                break;
            case "too-many-forms":
                context.Chest[1].Itemstack!.StackSize = 1;
                context.Fixture.BackpackInventory[4].Itemstack = new ItemStack(CreateForm(38, "nugget-metal-c", metalC), 2);
                context.Fixture.BackpackInventory[5].Itemstack = new ItemStack(CreateForm(39, "chunk-metal-c", metalC), 1);
                break;
        }
        var beforeExecution = external.Select(slot => (Stack: slot.Itemstack, Amount: slot.StackSize)).ToArray();
        var totals = external.Where(slot => !slot.Empty).GroupBy(slot => slot.Itemstack!.Collectible.Code)
            .ToDictionary(group => group.Key, group => group.Sum(slot => slot.StackSize));
        AlloyDepositResultCode expected = change switch
        {
            "unchanged" => AlloyDepositResultCode.Success,
            "too-many-forms" => AlloyDepositResultCode.InsufficientSpace,
            _ => AlloyDepositResultCode.InsufficientItems
        };

        Assert.Equal(expected, Execute(context, plan));
        Assert.Same(fuel, context.Target[0].Itemstack);
        Assert.Equal(4, fuel.StackSize);
        foreach (var total in totals)
            Assert.Equal(total.Value, external.Concat(context.Target.CookingSlots)
                .Where(slot => slot.Itemstack?.Collectible.Code == total.Key).Sum(slot => slot.StackSize));
        if (change == "unchanged")
        {
            Assert.Equal([10, 3, 4, 4], context.Target.CookingSlots.Select(slot => slot.StackSize));
            Assert.Equal([aNuggets.Code, bBits.Code, bNuggets.Code, cBits.Code],
                context.Target.CookingSlots.Select(slot => slot.Itemstack!.Collectible.Code));
            Assert.Equal(15, context.Fixture.BackpackInventory[0].StackSize);
            Assert.Equal(5, context.Fixture.BackpackInventory[2].StackSize);
            Assert.Equal(6, context.Chest[1].StackSize);
            context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Exactly(4));
            Assert.Equal(AlloyDepositResultCode.Success, Execute(context, plan));
            // A completed plan should retain its contents, not send another round of transfers.
            context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Exactly(4));
            Assert.Equal([10, 3, 4, 4], context.Target.CookingSlots.Select(slot => slot.StackSize));
        }
        else
        {
            for (int index = 0; index < external.Length; index++)
            {
                Assert.Same(beforeExecution[index].Stack, external[index].Itemstack);
                Assert.Equal(beforeExecution[index].Amount, external[index].StackSize);
            }
            Assert.All(context.Target.CookingSlots, slot => Assert.True(slot.Empty));
            context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
        }

        MockItem CreateForm(int id, string name, MockItem ingot)
        {
            MockItem item = CreateItem(context.Fixture, id, name);
            item.CombustibleProps = new CombustibleProperties
            {
                SmeltedRatio = 20,
                SmeltedStack = new JsonItemStack { Code = ingot.Code, ResolvedItemstack = new ItemStack(ingot) }
            };
            return item;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Molybdochalkos_OpenedSuppliesWithLeadIngot_DepositsOnlyFittingNuggets(bool playerSupplies)
    {
        Context context = CreateContext(0, 0);
        context.Target[1].Itemstack!.Collectible.Attributes = JsonObject.FromJson(
            "{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64,\"maxContentDimensions\":{\"width\":0.125,\"height\":0.25,\"length\":0.125}}");
        context.Metal.Dimensions = new Size3f(0.1f, 0.1f, 0.1f);
        MockItem lead = CreateItem(context.Fixture, 40, "ingot-lead");
        lead.CombustibleProps = new CombustibleProperties
        {
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack { Code = lead.Code, ResolvedItemstack = new ItemStack(lead) }
        };
        MockItem galena = CreateItem(context.Fixture, 41, "nugget-galena");
        galena.Dimensions = context.Metal.Dimensions;
        galena.CombustibleProps = lead.CombustibleProps;
        context.Chest[0].Itemstack = new ItemStack(lead);
        var leadIngredient = new MetalDepositIngredient(lead.Code, new ItemStack(lead), 0.88f, 0.90f);
        IInventory supply = playerSupplies ? context.Fixture.BackpackInventory! : context.Chest;
        // The chest has two slots; use its second slot for lead and player inventory for copper.
        supply[playerSupplies ? 0 : 1]!.Itemstack = new ItemStack(galena, 128);
        context.Fixture.HotbarInventory![0].Itemstack = new ItemStack(context.Metal, 14);
        var requested = new Dictionary<int, ItemStack>
        {
            [0] = new(context.Metal, 14),
            [1] = new(galena, 106)
        };
        Assert.False(context.Target.CookingSlots[0].CanHold(context.Chest[0]));
        Assert.Equal(AlloyDepositResultCode.Success, AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, [context.Ingredient, leadIngredient],
            requested, context.Fixture.Player, out AlloyDepositPlan? plan));
        Assert.DoesNotContain(plan!.Targets, target => target.DesiredStack!.Collectible.Code == lead.Code);
        Assert.Equal(AlloyDepositResultCode.Success, Execute(context, plan));
        Assert.Equal(14, context.Target.CookingSlots.Where(slot => slot.Itemstack?.Collectible == context.Metal)
            .Sum(slot => slot.StackSize));
        Assert.Equal(106, context.Target.CookingSlots.Where(slot => slot.Itemstack?.Collectible == galena)
            .Sum(slot => slot.StackSize));
        Assert.Equal(1, context.Chest[0].StackSize);
        Assert.Equal(22, supply[playerSupplies ? 0 : 1]!.StackSize);

        foreach (ItemSlot slot in context.Target.CookingSlots) slot.Itemstack = null;
        context.Fixture.HotbarInventory[0].Itemstack = new ItemStack(context.Metal, 14);
        supply[playerSupplies ? 0 : 1]!.Itemstack = new ItemStack(galena, 105);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, [context.Ingredient, leadIngredient],
            requested, context.Fixture.Player, out _));
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, Execute(context, plan));
        Assert.Equal(1, context.Chest[0].StackSize);
        Assert.All(context.Target.CookingSlots, slot => Assert.True(slot.Empty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TinBronze_WithAdditionalTinFormInOpenedChest_DepositsExactAmounts(bool chestOpen)
    {
        Context context = CreateContext(18, 0);
        MockItem tinIngot = CreateItem(context.Fixture, 20, "ingot-tin");
        MockItem tinNugget = CreateItem(context.Fixture, 21, "nugget-tin");
        MockItem tinBit = CreateItem(context.Fixture, 22, "metalbit-tin");
        tinNugget.CombustibleProps = new CombustibleProperties
        {
            SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack
            {
                Code = tinIngot.Code,
                ResolvedItemstack = new ItemStack(tinIngot)
            }
        };
        tinBit.CombustibleProps = tinNugget.CombustibleProps;
        context.Fixture.BackpackInventory[1].Itemstack = new ItemStack(tinNugget, 2);
        context.Chest[0].Itemstack = new ItemStack(tinBit, 1);
        if (!chestOpen) context.Opened.Remove(context.Chest);
        var tinIngredient = new MetalDepositIngredient(tinIngot.Code, new ItemStack(tinIngot), 0.1f, 0.1f);

        Assert.Equal(AlloyDepositResultCode.Success, AlloyDepositService.CreatePlan(
            context.Fixture.ClientApi, context.Firepit, [context.Ingredient, tinIngredient],
            new Dictionary<int, ItemStack>
            {
                [0] = new(context.Ingot, 18),
                [1] = new(tinIngot, 2)
            },
            context.Fixture.Player, out AlloyDepositPlan? plan));
        Assert.Equal(AlloyDepositResultCode.Success, Execute(context, plan!));
        Assert.Equal(18, context.Target.CookingSlots
            .Where(slot => slot.Itemstack?.Collectible.Code == context.Metal.Code).Sum(slot => slot.StackSize));
        Assert.Equal(2, context.Target.CookingSlots
            .Where(slot => slot.Itemstack?.Collectible.Code == tinNugget.Code
                || slot.Itemstack?.Collectible.Code == tinBit.Code).Sum(slot => slot.StackSize));
        Assert.Equal(1, context.Chest.Concat(context.Fixture.BackpackInventory)
            .Concat(context.Fixture.HotbarInventory).Sum(slot => slot.StackSize));
        if (chestOpen) Assert.True(context.Chest[0].Empty);
    }

    /// <summary>Verifies container-only and combined supplies conserve items and use vanilla packets.</summary>
    [Theory]
    [InlineData(0, 5)]
    [InlineData(2, 3)]
    public void OpenedIngredients_CreateAndExecutePlan_ConservesSupply(int playerAmount, int containerAmount)
    {
        Context context = CreateContext(playerAmount, containerAmount);
        Assert.Equal(AlloyDepositResultCode.Success, CreatePlan(context, 5, out var plan));
        Assert.Equal(AlloyDepositResultCode.Success, Execute(context, plan!));
        Assert.Equal(5, context.Target.CookingSlots.Sum(s => s.StackSize));
        Assert.Equal(0, context.Chest[0].StackSize);
        Assert.Equal(0, context.Fixture.BackpackInventory[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.AtLeastOnce);
    }

    /// <summary>Verifies repeated opened references and target aliases cannot inflate available ingredients.</summary>
    [Fact]
    public void DuplicateOpenedInventory_DoesNotInflateAvailability()
    {
        Context context = CreateContext(0, 2);
        context.Opened.Add(context.Chest);
        context.Opened.Add(context.Target);
        context.Fixture.BackpackInventory[0] = context.Target.CookingSlots[0];
        context.Target.CookingSlots[0].Itemstack = new ItemStack(context.Metal, 1);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, CreatePlan(context, 4, out _));
    }

    /// <summary>Verifies take locks prevent planning even while the storage remains open.</summary>
    [Fact]
    public void TakeLockedContainer_IngredientsAreUnavailable()
    {
        Context context = CreateContext(0, 5);
        context.Chest.TakeLocked = true;
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, CreatePlan(context, 5, out _));
    }

    /// <summary>Verifies execution collects current inventories rather than retaining planning references.</summary>
    [Fact]
    public void ContainerClosedAfterPlan_RejectsUnavailableIngredients()
    {
        Context context = CreateContext(0, 5);
        Assert.Equal(AlloyDepositResultCode.Success, CreatePlan(context, 5, out var plan));
        context.Opened.Remove(context.Chest);
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, Execute(context, plan!));
        Assert.Equal(5, context.Chest[0].StackSize);
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    #endregion

    #region Correction Returns

    /// <summary>Verifies both wrong stacks and matching excess can return to storage when player slots are full.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullPlayerInventory_ReturnsCorrectionsToOpenedContainer(bool wrongMetal)
    {
        Context context = CreateContext(0, wrongMetal ? 5 : 0);
        MockItem other = CreateItem(context.Fixture, 30, "other");
        foreach (ItemSlot slot in context.Fixture.BackpackInventory.Concat(context.Fixture.HotbarInventory))
            slot.Itemstack = new ItemStack(other, 64);
        context.Target.CookingSlots[0].Itemstack = new ItemStack(wrongMetal ? other : context.Metal, 8);
        var plan = new AlloyDepositPlan([new AlloyDepositSlotTarget(0, context.Ingredient, 5)]);
        Assert.Equal(AlloyDepositResultCode.Success, Execute(context, plan));
        Assert.Equal(5, context.Target.CookingSlots[0].StackSize);
        Assert.Equal(wrongMetal ? 8 : 3, context.Chest.Sum(s => s.StackSize));
        Assert.Equal(context.Metal.Code, context.Target.CookingSlots[0].Itemstack!.Collectible.Code);
    }

    /// <summary>Verifies a put lock prevents returns without losing items or sending a movement packet.</summary>
    [Fact]
    public void PutLockedContainerAndFullPlayerInventory_ReturnsInsufficientSpace()
    {
        Context context = CreateContext(0, 0);
        MockItem other = CreateItem(context.Fixture, 30, "other");
        foreach (ItemSlot slot in context.Fixture.BackpackInventory.Concat(context.Fixture.HotbarInventory))
            slot.Itemstack = new ItemStack(other, 64);
        context.Chest.PutLocked = true;
        context.Target.CookingSlots[0].Itemstack = new ItemStack(context.Metal, 8);
        Assert.Equal(AlloyDepositResultCode.InsufficientSpace, Execute(context, new AlloyDepositPlan([new AlloyDepositSlotTarget(0, context.Ingredient, 5)])));
        Assert.Equal(8, context.Target.CookingSlots[0].StackSize);
        Assert.All(context.Chest, s => Assert.True(s.Empty));
        context.Fixture.ClientNetworkMock!.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Never);
    }

    #endregion

    #region Transfer Lifecycle

    /// <summary>Verifies closing storage after one packet preserves that movement and stops further withdrawals.</summary>
    [Fact]
    public void ContainerClosesDuringPacket_ReturnsPartialDeficit()
    {
        Context context = CreateContext(0, 5);
        Assert.Equal(AlloyDepositResultCode.Success, CreatePlan(context, 5, out var plan));
        context.Fixture.ClientNetworkMock!.Setup(n => n.SendPacketClient(It.IsAny<object>()))
            .Callback(() => context.Opened.Remove(context.Chest));
        Assert.Equal(AlloyDepositResultCode.InsufficientItems, Execute(context, plan!));
        Assert.Equal(2, context.Target.CookingSlots.Sum(s => s.StackSize));
        Assert.Equal(3, context.Chest[0].StackSize);
        context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Once);
    }

    /// <summary>Verifies closing the target after a transfer reports closure and retains the completed movement.</summary>
    [Fact]
    public void TargetClosesDuringPacket_StopsSubsequentMovements()
    {
        Context context = CreateContext(0, 5);
        Assert.Equal(AlloyDepositResultCode.Success, CreatePlan(context, 5, out var plan));
        context.Fixture.ClientNetworkMock!.Setup(n => n.SendPacketClient(It.IsAny<object>()))
            .Callback(() => context.Opened.Remove(context.Target));
        Assert.Equal(AlloyDepositResultCode.InventoryClosed, Execute(context, plan!));
        Assert.Equal(2, context.Target.CookingSlots.Sum(s => s.StackSize));
        Assert.Equal(3, context.Chest[0].StackSize);
        context.Fixture.ClientNetworkMock.Verify(n => n.SendPacketClient(It.IsAny<object>()), Times.Once);
    }

    #endregion

    #endregion

    #region Private

    /// <summary>Builds a requested ingredient plan through the fixture inventory manager.</summary>
    private static AlloyDepositResultCode CreatePlan(Context context, int amount, out AlloyDepositPlan? plan)
    {
        return AlloyDepositService.CreatePlan(context.Fixture.ClientApi, context.Firepit, [context.Ingredient],
            new Dictionary<int, ItemStack> { [0] = new(context.Ingot, amount) }, context.Fixture.Player, out plan);
    }

    /// <summary>Executes a plan against the context's current opened-inventory state.</summary>
    private static AlloyDepositResultCode Execute(Context context, AlloyDepositPlan plan)
    {
        return AlloyDepositService.ExecutePlan(context.Fixture.ClientApi, context.Firepit, plan, context.Fixture.Player);
    }

    /// <summary>Creates a stackable item with stable matching identity.</summary>
    private static MockItem CreateItem(VsTestFixture fixture, int id, string name)
    {
        MockItem item = fixture.CreateNonLightSource(id);
        item.Code = new AssetLocation("game", name);
        item.MaxStackSize = 64;
        return item;
    }

    /// <summary>Creates an open firepit and an owner-backed storage inventory sharing one copper supply.</summary>
    private static Context CreateContext(int playerAmount, int containerAmount)
    {
        VsTestFixture fixture = VsTestFixture.Client();
        var accessor = new Mock<IBlockAccessor>();
        fixture.WorldMock.SetupGet(w => w.BlockAccessor).Returns(accessor.Object);
        MockItem ingot = CreateItem(fixture, 10, "ingot-copper");
        MockItem metal = CreateItem(fixture, 11, "metalbit-copper");
        metal.CombustibleProps = new CombustibleProperties { SmeltedRatio = 1,
            SmeltedStack = new JsonItemStack { Code = ingot.Code, ResolvedItemstack = new ItemStack(ingot) } };
        var firepit = new BlockEntityFirepit { Api = fixture.Api, Pos = new BlockPos(0) };
        var target = Assert.IsType<InventorySmelting>(firepit.Inventory);
        target.Api = fixture.Api;
        target.InvNetworkUtil = fixture.InvNetworkUtilMock.Object;
        MockItem crucible = CreateItem(fixture, 12, "crucible");
        crucible.Attributes = JsonObject.FromJson("{\"cookingContainerSlots\":4,\"maxContainerSlotStackSize\":64}");
        target[1].Itemstack = new ItemStack(crucible);
        var chest = new InventoryGeneric(2, "chest", "opened", null!) {
            Api = fixture.Api, Pos = new BlockPos(1, 0, 0), InvNetworkUtil = fixture.InvNetworkUtilMock.Object };
        var owner = new Mock<BlockEntityGenericContainer>();
        owner.SetupGet(o => o.Inventory).Returns(chest);
        accessor.Setup(a => a.GetBlockEntity(chest.Pos)).Returns(owner.Object);
        if (playerAmount > 0) fixture.BackpackInventory[0].Itemstack = new ItemStack(metal, playerAmount);
        if (containerAmount > 0) chest[0].Itemstack = new ItemStack(metal, containerAmount);
        List<IInventory> opened = [target, chest];
        fixture.InventoryManagerMock.SetupGet(m => m.OpenedInventories).Returns(opened);
        return new Context(fixture, firepit, target, chest, opened, ingot, metal,
            new MetalDepositIngredient(ingot.Code, new ItemStack(ingot), 1, 1));
    }

    /// <summary>Owns the mutable inventory state used to exercise transfer lifecycle changes.</summary>
    private sealed record Context(VsTestFixture Fixture, BlockEntityFirepit Firepit, InventorySmelting Target,
        InventoryGeneric Chest, List<IInventory> Opened, MockItem Ingot, MockItem Metal, MetalDepositIngredient Ingredient);

    #endregion
}
