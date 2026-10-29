using System.Collections.Immutable;
using Moq;
using VanillaExpanded.CraftingMenu;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public class CraftingTransferTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 20)]
    public void BothModesDispatchNativePacketsWithoutTakingOutput(bool shift, int expected)
    {
        using var f = new TransferFixture(2, 20);
        var output = f.F.Stack(99, "output", 7);
        f.F.Grid[9].Itemstack = output;
        var result = f.Coordinator.Execute(f.Entry, shift);
        Assert.Equal(CraftingDispatchStatus.Dispatched, result.Status);
        Assert.Equal(expected, f.F.Grid[0].StackSize);
        Assert.Equal(20 - expected, f.F.Hotbar[0].StackSize);
        Assert.Same(output, f.F.Grid[9].Itemstack);
        Assert.Equal(7, output.StackSize);
        var packet = Assert.IsType<Packet_Client>(Assert.Single(f.Packets));
        Assert.Equal(8, packet.Id);
        Assert.Equal(expected, packet.MoveItemstack.Quantity);
        Assert.Equal(f.F.Hotbar.InventoryID, packet.MoveItemstack.SourceInventoryId);
        Assert.Equal(f.F.Grid.InventoryID, packet.MoveItemstack.TargetInventoryId);
        Assert.Empty(f.Notifications);
        Assert.Equal(0, f.Refreshes);
    }

    [Fact]
    public void ExistingGridIsReusedOnceEvenWithFullStorage()
    {
        using var f = new TransferFixture(2, 0);
        f.F.Grid[0].Itemstack = f.Ingredient(2);
        f.FillStorage();
        var before = f.F.Grid[0].Itemstack;
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Same(before, f.F.Grid[0].Itemstack);
        Assert.Empty(f.Packets);
    }

    [Fact]
    public void RepositioningDoesNotRequireReturningEveryGridStack()
    {
        using var f = new TransferFixture(2, 0);
        f.F.Grid[1].Itemstack = f.Ingredient(2);
        f.FillStorage();
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Equal(2, f.F.Grid[0].StackSize);
        Assert.True(f.F.Grid[1].Empty);
        Assert.Single(f.Packets);
    }

    [Fact]
    public void VacantInputCanFreeCarriedSpaceThroughNativeBufferMoves()
    {
        using var f = new TransferFixture(2, 2);
        f.FillStorage(skipFirst: true);
        f.F.Grid[0].Itemstack = f.F.Stack(2, "stone", 1);
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Equal(2, f.F.Grid[0].StackSize);
        Assert.Equal("stone", f.F.Hotbar[0].Itemstack!.Collectible.Code.Path);
        Assert.All(Enumerable.Range(1, 8), index => Assert.True(f.F.Grid[index].Empty));
        Assert.Equal(3, f.Packets.Count);
    }

    [Fact]
    public void InsufficientReturnCapacityIsAnUnchangedPlacementRefusalNotIngredientFailure()
    {
        using var f = new TransferFixture(2, 2);
        f.FillStorage(skipFirst: true);
        f.F.Grid[0].Itemstack = f.F.Stack(2, "stone", 1);
        for (int i = 1; i < 9; i++) f.F.Grid[i].Itemstack = f.F.Stack(300 + i, "grid-filled-" + i, 64);
        var before = f.CopyAll();
        var allocation = f.F.Allocator.Evaluate(f.Entry, f.Snapshot());
        Assert.True(allocation.Evaluation.CanCraft);
        var result = f.Coordinator.Execute(f.Entry, false);
        Assert.Equal(CraftingTransferPlanner.Space, result.Reason);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
    }

    [Fact]
    public void NoUsableIngredientsDoesNotClearUnrelatedGrid()
    {
        using var f = new TransferFixture(2, 0);
        f.F.Grid[0].Itemstack = f.F.Stack(2, "stone", 1);
        var before = f.CopyAll();
        Assert.Equal(CraftingTransferPlanner.Empty, f.Coordinator.Execute(f.Entry, true).Reason);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("flags")]
    [InlineData("limit")]
    [InlineData("closed")]
    public void NativeDestinationRestrictionsRefuseBeforeMutation(string restriction)
    {
        using var f = new TransferFixture(2, 2);
        if (restriction == "locked") f.F.Grid.PutLocked = true;
        if (restriction == "flags") f.F.Grid[0].StorageType = 0;
        if (restriction == "limit") f.F.Grid[0].MaxSlotStackSize = 0;
        if (restriction == "closed") f.F.Grid.Close(f.F.Player);
        var before = f.CopyAll();
        Assert.Equal(CraftingDispatchStatus.Rejected, f.Coordinator.Execute(f.Entry, false).Status);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
        Assert.Single(f.Notifications);
    }

    [Fact]
    public void HotDisplacedStackIsRefusedWithoutInvokingNativeDrop()
    {
        using var f = new TransferFixture(2, 2);
        var hot = f.F.Stack(2, "hot", 1);
        hot.Collectible.SetTemperature(f.F.Catalogue.World.Object, hot, 900);
        f.F.Grid[1].Itemstack = hot;
        var before = f.CopyAll();
        Assert.Equal(CraftingTransferPlanner.Unsafe, f.Coordinator.Execute(f.Entry, false).Reason);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 20)]
    public void PlacementTrustsCatalogueRecipeWithoutQueryingRegistry(bool shift, int expected)
    {
        using var f = new TransferFixture(2, 20);
        f.F.Catalogue.World.Setup(world => world.GridRecipes)
            .Throws(new InvalidOperationException("Placement must not recheck immutable catalogue membership."));
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, shift).Status);
        Assert.Equal(expected, f.F.Grid[0].StackSize);
    }

    [Fact]
    public void InventoryChangedAfterPreviewIsFreshlyRecomputed()
    {
        using var f = new TransferFixture(2, 20);
        Assert.Equal(2, f.F.Allocator.Evaluate(f.Entry, f.Snapshot()).Ordinary[0].Quantity);
        f.F.Hotbar[0].Itemstack!.StackSize = 1;
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Equal(1, f.F.Grid[0].StackSize);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("skill")]
    [InlineData("bag")]
    [InlineData("quantity")]
    [InlineData("duplicate")]
    [InlineData("destination")]
    [InlineData("source-overuse")]
    public void ForgedPlansAreRefusedWithoutLiveMutation(string defect)
    {
        using var f = new TransferFixture(2, 2);
        var source = new CraftingSlotId(f.F.Hotbar.InventoryID, 0);
        if (defect == "output") source = new(f.F.Grid.InventoryID, 9);
        if (defect == "skill") source = new(f.F.Hotbar.InventoryID, 10);
        if (defect == "bag") source = new(f.F.Backpack.InventoryID, 0);
        var quantity = defect == "quantity" ? -1 : defect == "source-overuse" ? 3 : 2;
        var stack = f.Ingredient(Math.Max(quantity, 1));
        var placement = new CraftingPlacement(defect == "destination" ? 9 : 0, stack,
            ImmutableArray.Create(new CraftingSourceAllocation(source, quantity)));
        var placements = defect == "duplicate" ? ImmutableArray.Create(placement, placement)
            : ImmutableArray.Create(placement);
        var allocation = f.F.Allocator.Evaluate(f.Entry, f.Snapshot()) with { Ordinary = placements };
        var before = f.CopyAll();
        var result = new CraftingTransferPlanner(f.F.Catalogue.World.Object).Plan(f.State(), allocation, false);
        Assert.False(result.IsValid);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
    }

    [Fact]
    public void ReentrantDispatchIsRejectedButNextClickDoesNotWaitForServer()
    {
        using var f = new TransferFixture(2, 20);
        CraftingDispatchResult? reentrant = null;
        f.OnSend = _ => reentrant = f.Coordinator.Execute(f.Entry, true);
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Equal(CraftingTransferCoordinator.Busy, reentrant!.Reason);
        f.OnSend = null;
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, true).Status);
        Assert.Equal(20, f.F.Grid[0].StackSize);
        Assert.Equal(2, f.Packets.Count);
    }

    [Fact]
    public void CompetingUpdateAfterFirstPacketStopsUnsentWorkAndKeepsAcceptedMove()
    {
        using var f = new TransferFixture(2, 4, positions: 2);
        f.OnSend = _ => f.F.Grid.PutLocked = true;
        var result = f.Coordinator.Execute(f.Entry, false);
        Assert.Equal(CraftingDispatchStatus.Interrupted, result.Status);
        Assert.Single(f.Packets);
        Assert.Equal(2, f.F.Grid[0].StackSize);
        Assert.True(f.F.Grid[1].Empty);
        Assert.Equal(4, f.F.Hotbar[0].StackSize + f.F.Grid[0].StackSize);
        Assert.Single(f.Notifications);
    }

    [Fact]
    public void ParentCloseAndDisposeRejectFurtherActions()
    {
        using var f = new TransferFixture(2, 2);
        f.F.Grid.Close(f.F.Player);
        Assert.Equal(CraftingTransferCoordinator.Stale, f.Coordinator.Execute(f.Entry, false).Reason);
        Assert.Empty(f.Packets);
        f.Coordinator.Dispose();
    }

    [Fact]
    public void NativeSlotChangesCoalesceIntoOneFreshInventoryRefresh()
    {
        using var f = new TransferFixture(2, 20);
        CraftingInventorySnapshot? refreshed = null;
        f.Coordinator.InventoryChanged += () => refreshed = f.Snapshot();
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Single(f.RefreshQueue);
        Assert.Equal(0, f.Refreshes);
        f.F.Hotbar[0].Itemstack!.StackSize = 3;
        f.F.Hotbar.DidModifyItemSlot(f.F.Hotbar[0]);
        f.F.Backpack.DidModifyItemSlot(f.F.Backpack[4]);
        Assert.Single(f.RefreshQueue);
        f.Refresh();
        Assert.Equal(1, f.Refreshes);
        Assert.NotNull(refreshed);
        Assert.Equal(3, refreshed.Stacks.Single(stack => stack.Slot == new CraftingSlotId(f.F.Hotbar.InventoryID, 0)).Quantity);
        Assert.Equal(2, refreshed.Stacks.Single(stack => stack.Slot == new CraftingSlotId(f.F.Grid.InventoryID, 0)).Quantity);
        Assert.Empty(f.Notifications);
    }

    [Fact]
    public void ExcludedSlotsAndRemoteInventoriesDoNotInvalidatePreviews()
    {
        using var f = new TransferFixture(2, 2);
        f.F.Hotbar.DidModifyItemSlot(f.F.Hotbar[10]);
        f.F.Hotbar.DidModifyItemSlot(f.F.Hotbar[11]);
        f.F.Backpack.DidModifyItemSlot(f.F.Backpack[0]);
        f.F.Grid.DidModifyItemSlot(f.F.Grid[9]);
        var remote = new InventoryGeneric(1, "remote", "test", null!) { Api = f.F.Api.Object };
        remote.DidModifyItemSlot(remote[0]);
        Assert.Empty(f.RefreshQueue);
        Assert.Equal(0, f.Refreshes);
    }

    [Fact]
    public void ClosingDisposesSubscriptionsAndCancelsQueuedRefreshes()
    {
        using var f = new TransferFixture(2, 2);
        f.Coordinator.Execute(f.Entry, false);
        Assert.Single(f.RefreshQueue);
        f.F.Grid.Close(f.F.Player);
        f.Refresh();
        Assert.Equal(0, f.Refreshes);
        f.F.Hotbar.DidModifyItemSlot(f.F.Hotbar[0]);
        f.F.Backpack.DidModifyItemSlot(f.F.Backpack[4]);
        f.F.Grid.DidModifyItemSlot(f.F.Grid[0]);
        Assert.Empty(f.RefreshQueue);
    }

    [Fact]
    public void LocalInventoryReplacementDuringPreflightIsRejectedBeforeDispatch()
    {
        using var f = new TransferFixture(2, 2);
        f.F.Events.Setup(events => events.TriggerMatchesRecipe(It.IsAny<IPlayer>(), It.IsAny<IRecipeBase>(),
            It.IsAny<ItemSlot[]>())).Returns(() =>
            {
                f.F.Hotbar[0].Itemstack = f.Ingredient(1);
                return true;
            });
        using var coordinator = new CraftingTransferCoordinator(f.F.Player, f.F.Catalogue.World.Object, f.F.Allocator,
            f.Packets.Add, f.Notifications.Add, f.RefreshQueue.Enqueue, Environment.CurrentManagedThreadId, true);
        var result = coordinator.Execute(f.Entry, false);
        Assert.Equal(CraftingDispatchStatus.Rejected, result.Status);
        Assert.Empty(f.Packets);
        Assert.True(f.F.Grid[0].Empty);
        Assert.Equal(1, f.F.Hotbar[0].StackSize);
    }

    [Fact]
    public void TemporaryMetadataIsNotSilentlyLostByNativeWholeStackSplitting()
    {
        using var f = new TransferFixture(2, 2);
        f.F.Hotbar[0].Itemstack!.TempAttributes.SetString("session", "important");
        var before = f.CopyAll();
        Assert.Equal(CraftingTransferPlanner.Unsafe, f.Coordinator.Execute(f.Entry, false).Reason);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
    }

    [Fact]
    public void FullStorageGridCycleUsesAVacantInputAsBuffer()
    {
        using var f = new TransferFixture(1, 0);
        var board = f.Ingredient(1);
        var stone = f.F.Stack(2, "stone", 1);
        var recipe = f.F.Recipe(f.F.Ingredient(board), f.F.Ingredient(stone));
        f.F.Register(recipe);
        f.F.Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe> { recipe });
        var entry = f.F.Catalogue.Build(recipe).Recipes[0];
        f.FillStorage();
        f.F.Grid[0].Itemstack = stone;
        f.F.Grid[1].Itemstack = board;
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(entry, false).Status);
        Assert.Equal("board", f.F.Grid[0].Itemstack!.Collectible.Code.Path);
        Assert.Equal("stone", f.F.Grid[1].Itemstack!.Collectible.Code.Path);
        Assert.All(Enumerable.Range(2, 7), index => Assert.True(f.F.Grid[index].Empty));
        Assert.Equal(3, f.Packets.Count);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("skill")]
    [InlineData("offhand")]
    [InlineData("equipped-bag")]
    [InlineData("locked-source")]
    public void ExcludedOrUnavailableStacksAreNeverMoved(string location)
    {
        using var f = new TransferFixture(2, 0);
        var stack = f.Ingredient(2);
        if (location == "output") f.F.Grid[9].Itemstack = stack;
        if (location == "skill") f.F.Hotbar[10].Itemstack = stack;
        if (location == "offhand") f.F.Hotbar[11].Itemstack = stack;
        if (location == "equipped-bag") f.F.Backpack[0].Itemstack = stack;
        if (location == "locked-source")
        {
            f.F.Hotbar[0].Itemstack = stack;
            f.F.Hotbar.TakeLocked = true;
        }
        var before = f.CopyAll();
        Assert.Equal(CraftingTransferPlanner.Empty, f.Coordinator.Execute(f.Entry, false).Reason);
        f.AssertSame(before);
        Assert.Empty(f.Packets);
    }

    [Theory]
    [InlineData("modified", false, false)]
    [InlineData("modified", false, true)]
    [InlineData("modified", true, false)]
    [InlineData("modified", true, true)]
    [InlineData("transition", false, false)]
    [InlineData("transition", false, true)]
    [InlineData("transition", true, false)]
    [InlineData("transition", true, true)]
    [InlineData("native-transition", false, false)]
    [InlineData("native-transition", false, true)]
    [InlineData("native-transition", true, false)]
    [InlineData("native-transition", true, true)]
    [InlineData("behavior", false, false)]
    [InlineData("behavior", false, true)]
    [InlineData("behavior", true, false)]
    [InlineData("behavior", true, true)]
    [InlineData("has-temperature", false, false)]
    [InlineData("has-temperature", false, true)]
    [InlineData("has-temperature", true, false)]
    [InlineData("has-temperature", true, true)]
    [InlineData("set-temperature", false, false)]
    [InlineData("set-temperature", false, true)]
    [InlineData("set-temperature", true, false)]
    [InlineData("set-temperature", true, true)]
    public void MovementCallbackOverridesDoNotBlanketRejectNativeTransfers(string callback, bool displaced, bool shift)
    {
        using var f = new TransferFixture(2, 2);
        int id = displaced ? 2 : 1;
        MockItem item = callback switch
        {
            "modified" => new ModificationCallbackItem(id),
            "transition" => new TransitionCallbackItem(id),
            "behavior" => new BehaviorCallbackItem(id),
            "has-temperature" => new HasTemperatureCallbackItem(id),
            "set-temperature" => new SetTemperatureCallbackItem(id),
            _ => new NativeTransitionCallbackItem(id)
        };
        item.Code = new AssetLocation(displaced ? "game:displaced" : "game:board");
        item.MaxStackSize = 64;
        var stack = new ItemStack(item, displaced ? 1 : 2);
        if (displaced) f.F.Grid[1].Itemstack = stack;
        else f.F.Hotbar[0].Itemstack = stack;
        var result = f.Coordinator.Execute(f.Entry, shift);
        Assert.Equal(CraftingDispatchStatus.Dispatched, result.Status);
        Assert.NotEmpty(f.Packets);
        Assert.Equal(2, f.F.Grid[0].StackSize);
        Assert.Empty(f.Notifications);
    }

    private sealed class ModificationCallbackItem(int id) : MockItem(id)
    {
        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null!) =>
            base.OnModifiedInInventorySlot(world, slot, extractedStack);
    }

    private sealed class TransitionCallbackItem(int id) : MockItem(id)
    {
        public override TransitionState[] UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot slot) =>
            base.UpdateAndGetTransitionStates(world, slot);
    }

    private sealed class NativeTransitionCallbackItem(int id) : MockItem(id)
    {
        protected override TransitionState[] UpdateAndGetTransitionStatesNative(IWorldAccessor world, ItemSlot slot) =>
            base.UpdateAndGetTransitionStatesNative(world, slot);
    }

    private sealed class BehaviorCallbackItem(int id) : MockItem(id)
    {
        protected override CollectibleBehavior GetBehavior(CollectibleBehavior[] behaviors, Type type, bool withInheritance) =>
            base.GetBehavior(behaviors, type, withInheritance);
    }

    private sealed class SetTemperatureCallbackItem(int id) : MockItem(id)
    {
        public override void SetTemperature(IWorldAccessor world, ItemStack stack, float temperature, bool delayCooldown = true) =>
            base.SetTemperature(world, stack, temperature, delayCooldown);
    }

    private sealed class HasTemperatureCallbackItem(int id) : MockItem(id)
    {
        public override bool HasTemperature(IItemStack stack) =>
            base.HasTemperature(stack);
    }
}

internal sealed class TransferFixture : IDisposable
{
    internal readonly AllocationFixture F = new();
    internal readonly List<object> Packets = new();
    internal readonly List<CraftingDispatchResult> Notifications = new();
    internal readonly CraftingRecipeEntry Entry;
    internal Action<object>? OnSend;
    internal int Refreshes;
    internal readonly Queue<Action> RefreshQueue = new();
    internal readonly CraftingTransferCoordinator Coordinator;
    internal readonly Mock<IClientNetworkAPI> Network = new();
    private readonly ItemStack ingredient;

    internal TransferFixture(int demand, int stock, int positions = 1)
    {
        ingredient = F.Stack(1, "board", 1);
        var recipe = F.Recipe(Enumerable.Range(0, positions).Select(_ => F.Ingredient(ingredient, demand)).ToArray());
        F.Register(recipe);
        F.Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe> { recipe });
        F.Catalogue.World.Setup(world => world.Calendar).Returns(Mock.Of<IGameCalendar>());
        Entry = F.Catalogue.Build(recipe).Recipes[0];
        if (stock > 0) F.Hotbar[0].Itemstack = Ingredient(stock);
        Network.Setup(network => network.SendPacketClient(It.IsAny<object>())).Callback<object>(packet =>
        {
            Packets.Add(packet);
            OnSend?.Invoke(packet);
        });
        Coordinator = new(F.Player, F.Catalogue.World.Object, F.Allocator,
            Network.Object.SendPacketClient, Notifications.Add, RefreshQueue.Enqueue, Environment.CurrentManagedThreadId, true);
        Coordinator.InventoryChanged += () => Refreshes++;
    }

    internal ItemStack Ingredient(int count)
    {
        var result = CraftingStackSnapshot.Copy(ingredient);
        result.StackSize = count;
        return result;
    }

    internal CraftingInventorySnapshot Snapshot() =>
        CraftingInventorySnapshot.Capture(F.Player, 3, 3, Environment.CurrentManagedThreadId, true);
    internal CraftingTransferState State() => new(F.Player, F.Catalogue.World.Object, Snapshot(), true);
    internal void Refresh()
    {
        while (RefreshQueue.TryDequeue(out var refresh)) refresh();
    }
    internal void FillStorage(bool skipFirst = false)
    {
        for (int i = skipFirst ? 1 : 0; i < 10; i++) F.Hotbar[i].Itemstack = F.Stack(100 + i, "filled-" + i, 64);
        for (int i = 4; i < F.Backpack.Count; i++) F.Backpack[i].Itemstack = F.Stack(200 + i, "filled-" + i, 64);
    }

    internal ItemStack?[] CopyAll() => new[] { F.Hotbar, F.Backpack, F.Grid }.SelectMany(inv =>
        Enumerable.Range(0, inv.Count).Select(i => CraftingTransferState.Copy(inv[i].Itemstack))).ToArray();

    internal void AssertSame(ItemStack?[] before)
    {
        var after = CopyAll();
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
            Assert.True(CraftingTransferState.Same(F.Catalogue.World.Object, before[i], after[i]));
    }
    public void Dispose() => Coordinator.Dispose();
}
