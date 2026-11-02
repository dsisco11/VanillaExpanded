using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookTooltipSummaryTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class RecipeBookAllocationSummaryTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(3, 1, 3, 1)]
    [InlineData(64, 64, 4, 2)]
    public void SummaryUsesOneCraftCoverageRatherThanStockOrShiftPlacement(
        int boards, int sticks, int haveBoards, int haveSticks)
    {
        var f = new AllocationFixture();
        var board = NamedStack(f, 1, "board", boards);
        var stick = NamedStack(f, 2, "stick", sticks);
        var recipe = f.Recipe(f.Ingredient(board, 4), f.Ingredient(stick, 2));
        var selection = Selection(recipe, f.Allocate(recipe, board, stick));

        Assert.Equal(new[]
        {
            new CraftingIngredientSummary(board.GetName(), haveBoards, 4),
            new CraftingIngredientSummary(stick.GetName(), haveSticks, 2)
        }, RecipeBookTooltip.IngredientSummary(selection));
        Assert.Empty(RecipeBookTooltip.Warnings(selection));
    }

    [Fact]
    public void RepeatedRequirementsDoNotDoubleCountSharedStock()
    {
        var f = new AllocationFixture();
        var board = NamedStack(f, 1, "board", 3);
        var recipe = f.Recipe(f.Ingredient(board, 2), f.Ingredient(board, 2));
        var summary = RecipeBookTooltip.IngredientSummary(Selection(recipe, f.Allocate(recipe, board)));

        Assert.Equal(2, summary.Length);
        Assert.Equal(3, summary.Sum(ingredient => ingredient.Have));
        Assert.All(summary, ingredient => Assert.Equal(2, ingredient.Need));
        Assert.Equal(1, Assert.Single(summary, ingredient => ingredient.Have < ingredient.Need).Have);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(1, 0)]
    public void ToolCoverageCountsUsablePresenceNotRawPossession(int durability, int have)
    {
        var f = new AllocationFixture();
        var tool = NamedStack(f, 1, "saw", 1, maxStack: 1);
        tool.Collectible.Durability = 10;
        tool.Attributes.SetInt("durability", durability);
        var ingredient = f.Ingredient(tool);
        ingredient.ResolvedItemStack!.Attributes.RemoveAttribute("durability");
        ingredient.IsTool = true;
        ingredient.ToolDurabilityCost = 2;
        var recipe = f.Recipe(ingredient);
        var summary = Assert.Single(RecipeBookTooltip.IngredientSummary(
            Selection(recipe, f.Allocate(recipe, tool))));

        Assert.Equal(have, summary.Have);
        Assert.Equal(1, summary.Need);
    }

    [Fact]
    public void WildcardNameDoesNotMisrepresentAnAcceptedVariantAsTheRequirement()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 1);
        var recipe = f.Recipe(f.Wildcard("board-*", 2));
        var summary = Assert.Single(RecipeBookTooltip.IngredientSummary(
            Selection(recipe, f.Allocate(recipe, oak)), key =>
            {
                Assert.Equal("vanillaexpanded:recipebook-ingredient-choice", key);
                return "Matching ingredient";
            }));

        Assert.Equal(new CraftingIngredientSummary("Matching ingredient", 1, 2), summary);
    }
}
