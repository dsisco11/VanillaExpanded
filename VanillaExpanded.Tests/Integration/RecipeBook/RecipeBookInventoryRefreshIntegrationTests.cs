using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookInventoryRefreshIntegrationTests
{
    [Fact]
    public void DetectionCombinesGridAndCarriedItemsAndRefreshesWhenGridInputsChange()
    {
        using var f = new TransferFixture(6, 1);
        f.F.Backpack[4].Itemstack = f.Ingredient(2);
        f.F.Grid[8].Itemstack = f.Ingredient(3);
        f.F.Grid[9].Itemstack = f.Ingredient(64);
        var session = new RecipeBookSession(f.F.Catalogue.Build(f.Entry.Recipe), f.F.Allocator,
            f.Snapshot, f.Coordinator.Execute);
        f.Coordinator.InventoryChanged += session.Refresh;
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.True(row.Evaluation.CanCraft);
        Assert.Equal(6, row.Evaluation.Allocated);
        var sources = Assert.Single(row.Allocation!.Ordinary).Sources;
        Assert.Equal(new[] { f.F.Hotbar.InventoryID, f.F.Backpack.InventoryID, f.F.Grid.InventoryID },
            sources.Select(source => source.Slot.InventoryId));
        Assert.Equal(new[] { 1, 2, 3 }, sources.Select(source => source.Quantity));

        f.F.Hotbar[0].Itemstack = null;
        f.F.Grid[8].Itemstack = f.Ingredient(4);
        f.F.Hotbar.DidModifyItemSlot(f.F.Hotbar[0]);
        f.F.Grid.DidModifyItemSlot(f.F.Grid[8]);
        Assert.Single(f.RefreshQueue);
        f.Refresh();
        Assert.True(Assert.Single(session.Rows).Evaluation.CanCraft);
        Assert.Equal(6, Assert.Single(session.Rows).Evaluation.Allocated);

        f.F.Grid[8].Itemstack = f.Ingredient(3);
        f.F.Grid.DidModifyItemSlot(f.F.Grid[8]);
        f.Refresh();
        row = Assert.Single(session.Rows);
        Assert.False(row.Evaluation.CanCraft);
        Assert.Equal(5, row.Evaluation.Allocated);
        Assert.Equal(1, Assert.Single(row.Evaluation.Missing).Quantity);
        Assert.Equal(64, f.F.Grid[9].StackSize);
        Assert.Empty(f.Packets);
    }
}
