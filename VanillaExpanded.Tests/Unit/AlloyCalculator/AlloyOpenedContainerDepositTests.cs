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
