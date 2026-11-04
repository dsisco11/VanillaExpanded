using System.Collections.Immutable;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingTransferTestsTestSupport;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class CraftingTransferNativeIntegrationTests
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
        // Arrange a competing native update that blocks the next transfer after one packet is accepted.
        using var f = new TransferFixture(2, 4, positions: 2);
        f.OnSend = _ => f.F.Grid.PutLocked = true;

        // Act: begin a two-position transfer and let the competing update interrupt it.
        var result = f.Coordinator.Execute(f.Entry, false);

        // Assert the accepted movement remains while unsent work is stopped.
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
}
