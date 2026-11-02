using System.Diagnostics;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookPerformanceTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class CraftingEvaluationCacheTests
{
    [Fact]
    public void NativeIngredientChecksAreSharedAcrossRecipePositionsAndRecipesButNotRefreshes()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 64);
        var recipes = Enumerable.Range(0, 100)
            .Select(_ => f.Recipe(Enumerable.Range(0, 9).Select(_ => f.Ingredient(board)).ToArray())).ToArray();
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Catalogue.Index.Add(predicate.Object, recipes.Cast<IRecipeBase>().ToList());
        var catalogue = f.Catalogue.Build(recipes);
        var snapshot = f.Snapshot(board);
        f.Allocator.EvaluateCatalogue(catalogue, snapshot);
        // Stock (64), ordinary placement (1), and bulk placement (7/8).
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.AtMost(4));
        int checks = predicate.Invocations.Count;
        Assert.True(checks > 0);
        f.Allocator.EvaluateCatalogue(catalogue, snapshot);
        Assert.Equal(checks * 2, predicate.Invocations.Count);
    }
}
