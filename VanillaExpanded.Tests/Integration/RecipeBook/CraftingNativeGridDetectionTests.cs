using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class CraftingNativeGridDetectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void NativeGridInputAloneEnablesRecipeDetectionAndAvailability(int index)
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 2);
        var recipe = f.Recipe(f.Ingredient(board, 2));
        f.Register(recipe);
        var catalogue = f.Catalogue.Build(recipe);
        var grid = new InventoryCraftingGrid(f.Grid.InventoryID, null!) { Api = f.Api.Object };
        var manager = (ClientPlayerInventoryManager)f.Player.InventoryManager;
        manager.Inventories.Remove(f.Hotbar.InventoryID);
        manager.Inventories.Remove(f.Backpack.InventoryID);
        manager.Inventories.Remove(f.Grid.InventoryID);
        manager.Inventories.Add(grid.InventoryID, grid);
        grid[index].Itemstack = board;
        grid[9].Itemstack = f.Stack(1, "board", 64);
        CraftingInventorySnapshot Capture() => CraftingInventorySnapshot.Capture(
            f.Player, 3, 3, Environment.CurrentManagedThreadId, false);

        var snapshot = Capture();
        var source = Assert.Single(snapshot.Stacks);
        Assert.Equal(new CraftingSlotId(grid.InventoryID, index), source.Slot);
        Assert.Same(recipe, Assert.Single(catalogue.FindCandidates(
            snapshot.Stacks.Select(stack => stack.GetStack()))).Recipe);
        var session = new RecipeBookSession(catalogue, f.Allocator, Capture,
            (_, _) => throw new InvalidOperationException("Detection must not dispatch placement."));
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.True(row.Evaluation.CanCraft);
        Assert.True(row.CanPlace);
        Assert.Equal(2, row.Evaluation.Allocated);
        Assert.Empty(row.Evaluation.Missing);
        Assert.Equal(source.Slot, Assert.Single(Assert.Single(row.Allocation!.Ordinary).Sources).Slot);

        grid[index].Itemstack = null;
        Assert.Empty(catalogue.FindCandidates(Capture().Stacks.Select(stack => stack.GetStack())));
        session.Refresh();
        row = Assert.Single(session.Rows);
        Assert.False(row.Evaluation.CanCraft);
        Assert.Empty(row.Allocation!.Ordinary);
        Assert.Equal(0, row.Evaluation.Allocated);
        Assert.Equal(2, Assert.Single(row.Evaluation.Missing).Quantity);
        Assert.Equal(64, grid[9].StackSize);
    }
}
