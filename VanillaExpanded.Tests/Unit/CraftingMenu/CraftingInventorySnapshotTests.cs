using VanillaExpanded.CraftingMenu;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public class CraftingInventorySnapshotTests
{
    [Fact]
    public void Capture_UsesOnlyOrdinaryHotbarBagContentsAndGridInputs()
    {
        var f = new AllocationFixture();
        for (int i = 0; i < f.Hotbar.Count; i++) f.Hotbar[i].Itemstack = f.Stack(i + 1, "hotbar-" + i, 1);
        for (int i = 0; i < f.Backpack.Count; i++) f.Backpack[i].Itemstack = f.Stack(i + 20, "bag-" + i, 1);
        for (int i = 0; i < f.Grid.Count; i++) f.Grid[i].Itemstack = f.Stack(i + 40, "grid-" + i, 1);
        var snapshot = CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true);

        Assert.Equal(23, snapshot.Stacks.Length);
        Assert.Equal(23, snapshot.Stacks.Select(stack => stack.Slot).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 10), snapshot.Stacks.Where(stack => stack.Slot.InventoryId == f.Hotbar.InventoryID)
            .Select(stack => stack.Slot.Index));
        Assert.Equal(Enumerable.Range(4, 4), snapshot.Stacks.Where(stack => stack.Slot.InventoryId == f.Backpack.InventoryID)
            .Select(stack => stack.Slot.Index));
        Assert.Equal(Enumerable.Range(0, 9), snapshot.Stacks.Where(stack => stack.Slot.InventoryId == f.Grid.InventoryID)
            .Select(stack => stack.Slot.Index));
    }

    [Fact]
    public void Capture_CopiesPersistentAndTemporaryDataAndReadsFreshEachTime()
    {
        var f = new AllocationFixture();
        var stack = f.Stack(1, "board", 5);
        stack.Attributes.SetInt("quality", 7);
        stack.TempAttributes.SetString("state", "old");
        var first = f.Snapshot(stack);
        stack.StackSize = 1;
        stack.Attributes.SetInt("quality", 1);
        stack.TempAttributes.SetString("state", "new");
        var copy = Assert.Single(first.Stacks).GetStack();
        Assert.Equal(5, copy.StackSize);
        Assert.Equal(7, copy.Attributes.GetInt("quality"));
        Assert.Equal("old", copy.TempAttributes.GetString("state"));
        copy.TempAttributes.SetString("state", "mutated");
        Assert.Equal("old", first.Stacks[0].GetStack().TempAttributes.GetString("state"));
        Assert.Equal(1, Assert.Single(f.Snapshot(stack).Stacks).Quantity);
    }

    [Fact]
    public void Capture_ExcludesLockedOrClosedInventories()
    {
        var f = new AllocationFixture();
        f.Hotbar.TakeLocked = true;
        f.Hotbar[0].Itemstack = f.Stack(1, "board", 1);
        f.Backpack.Close(f.Player);
        f.Backpack[4].Itemstack = f.Stack(2, "stick", 1);
        f.Grid[0].Itemstack = f.Stack(3, "clay", 1);
        var snapshot = CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true);
        Assert.Equal(f.Grid.InventoryID, Assert.Single(snapshot.Stacks).Slot.InventoryId);
    }

    [Fact]
    public void ExistingGridSupply_IsCountedOnce()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        f.Grid[0].Itemstack = board;
        var recipe = f.Recipe(f.Ingredient(board), f.Ingredient(board));
        var result = f.Allocate(recipe);
        Assert.Equal(1, result.Evaluation.Allocated);
        Assert.False(result.Evaluation.CanCraft);
        Assert.Single(result.Ordinary);
        Assert.Equal(f.Grid.InventoryID, Assert.Single(result.Ordinary[0].Sources).Slot.InventoryId);
    }

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
        var session = new CraftingMenuSession(catalogue, f.Allocator, Capture,
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

    [Fact]
    public void GridSlotLimit_CapsPlacementButNotOtherPositions()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 20);
        f.Grid[0].MaxSlotStackSize = 3;
        var result = f.Allocate(f.Recipe(f.Ingredient(board), f.Ingredient(board)), board);
        Assert.Equal(new[] { 3, 17 }, result.Shift.Select(placement => placement.Quantity));
        Assert.Equal(3, result.CompleteExecutions);
    }

    [Fact]
    public void Capture_RejectsWrongThreadAndUnknownAdapters()
    {
        var f = new AllocationFixture();
        Assert.Throws<InvalidOperationException>(() => CraftingInventorySnapshot.Capture(
            f.Player, 3, 3, -1, true));
        Assert.Throws<NotSupportedException>(() => CraftingInventorySnapshot.Capture(
            f.Player, 3, 3, Environment.CurrentManagedThreadId, false));
    }

    [Fact]
    public void Capture_RejectsAliasedPhysicalStacks()
    {
        var f = new AllocationFixture();
        var stack = f.Stack(1, "board", 1);
        Assert.Throws<InvalidOperationException>(() => f.Snapshot(stack, stack));
    }
}
