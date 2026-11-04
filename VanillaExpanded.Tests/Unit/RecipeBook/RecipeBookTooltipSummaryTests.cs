using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookTooltipSummaryTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class RecipeBookTooltipSummaryTests
{
    [Fact]
    public void UnsupportedOrUnevaluatedSelectionsDoNotInventIngredientCounts()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var recipe = f.Recipe(f.Ingredient(board, 2));
        var selection = Selection(recipe, f.Allocate(recipe, board));

        Assert.Empty(RecipeBookTooltip.IngredientSummary(selection with { Allocation = null }));
        Assert.Empty(RecipeBookTooltip.IngredientSummary(selection with
        {
            Evaluation = CraftingRecipeEvaluation.Unsupported("unsupported")
        }));
    }
}
