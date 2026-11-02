using Moq;
using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingAllocationTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class CraftingCatalogueEvaluationPipelineTests
{
    [Fact]
    public void FreshCatalogueEvaluation_WiresSelectionRankingAndPlans()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var stick = f.Stack(2, "stick", 1);
        var full = f.Recipe(f.Ingredient(board));
        var partial = f.Recipe(f.Ingredient(stick, 2));
        var unsupported = f.Recipe(f.Ingredient(board));
        unsupported.CopyAttributesFrom = "I";
        f.Register(full, partial, unsupported);
        var catalogue = f.Catalogue.Build(full, partial, unsupported);
        var view = new CraftingCatalogueView(catalogue);
        var results = f.Allocator.EvaluateCatalogue(catalogue, f.Snapshot(board, stick));
        view.ReplaceAllocations(results);
        using var filters = new RecipeFilterSet();
        var selections = view.GetRows(filters);

        Assert.Same(full, selections[0].Recipe.Recipe);
        Assert.Same(partial, selections[1].Recipe.Recipe);
        Assert.Same(unsupported, selections[2].Recipe.Recipe);
        Assert.Same(results[selections[0].Recipe.Id], selections[0].Allocation);
        Assert.Same(selections[0].Allocation!.Evaluation, selections[0].Evaluation);
        Assert.False(selections[2].CanPlace);
        Assert.Equal(1, Assert.Single(selections[1].Evaluation.Missing).Quantity);
        Assert.Equal(1, Assert.Single(selections[1].Allocation!.Ordinary).Quantity);

        view.ReplaceAllocations(f.Allocator.EvaluateCatalogue(catalogue, f.Snapshot()));
        Assert.All(view.GetRows(filters).Where(selection => selection.CanPlace), selection =>
        {
            Assert.Equal(0, selection.Evaluation.Allocated);
            Assert.Empty(selection.Allocation!.Ordinary);
        });
    }
}
