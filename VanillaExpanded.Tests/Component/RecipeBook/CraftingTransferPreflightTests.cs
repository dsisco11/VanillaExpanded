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
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingTransferTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class CraftingTransferPreflightTests
{
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
}
