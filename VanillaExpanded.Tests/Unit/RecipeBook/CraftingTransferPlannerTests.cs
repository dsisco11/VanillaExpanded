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

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingTransferPlannerTests
{
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
}
