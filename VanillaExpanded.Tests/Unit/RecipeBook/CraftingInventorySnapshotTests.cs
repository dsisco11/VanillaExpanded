using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingInventorySnapshotTests
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
        Assert.Equal(9, snapshot.GridStacks.Length);
        Assert.Equal(Enumerable.Range(0, 9), snapshot.GridStacks.Select(stack => stack.Slot.Index));
        Assert.All(snapshot.GridStacks, input =>
            Assert.Same(input, snapshot.Stacks.Single(stack => stack.Slot == input.Slot)));
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
    public void CapturedGridInputsAreIsolatedFromLaterEditsAndExcludeLockedInputsAndOutput()
    {
        var f = new AllocationFixture();
        var input = f.Stack(1, "board", 3);
        input.Attributes.SetInt("quality", 7);
        input.TempAttributes.SetBool("ready", true);
        f.Grid[8].Itemstack = input;
        f.Grid[9].Itemstack = CraftingStackSnapshot.Copy(input);
        var snapshot = f.Snapshot(input.Clone());
        input.StackSize = 64;
        input.Attributes.SetInt("quality", 1);
        input.TempAttributes.SetBool("ready", false);
        var captured = Assert.Single(snapshot.GridStacks).GetStack();
        Assert.Equal(3, captured.StackSize);
        Assert.Equal(7, captured.Attributes.GetInt("quality"));
        Assert.True(captured.TempAttributes.GetBool("ready"));
        captured.TempAttributes.SetBool("ready", false);
        Assert.True(snapshot.GridStacks[0].GetStack().TempAttributes.GetBool("ready"));
        f.Grid.TakeLocked = true;
        Assert.Empty(f.Snapshot().GridStacks);
        f.Grid.TakeLocked = false;
        f.Grid[8].Itemstack = null;
        Assert.Empty(f.Snapshot().GridStacks);
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
