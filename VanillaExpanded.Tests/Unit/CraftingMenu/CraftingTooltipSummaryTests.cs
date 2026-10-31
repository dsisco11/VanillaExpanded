using Moq;
using VanillaExpanded.CraftingMenu;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public sealed class CraftingTooltipSummaryTests
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
        }, CraftingMenuTooltip.IngredientSummary(selection));
        Assert.Empty(CraftingMenuTooltip.Warnings(selection));
    }

    [Fact]
    public void RepeatedRequirementsDoNotDoubleCountSharedStock()
    {
        var f = new AllocationFixture();
        var board = NamedStack(f, 1, "board", 3);
        var recipe = f.Recipe(f.Ingredient(board, 2), f.Ingredient(board, 2));
        var summary = CraftingMenuTooltip.IngredientSummary(Selection(recipe, f.Allocate(recipe, board)));

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
        var summary = Assert.Single(CraftingMenuTooltip.IngredientSummary(
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
        var summary = Assert.Single(CraftingMenuTooltip.IngredientSummary(
            Selection(recipe, f.Allocate(recipe, oak)), key =>
            {
                Assert.Equal("vanillaexpanded:craftingmenu-ingredient-choice", key);
                return "Matching ingredient";
            }));

        Assert.Equal(new CraftingIngredientSummary("Matching ingredient", 1, 2), summary);
    }

    [Fact]
    public void UnsupportedOrUnevaluatedSelectionsDoNotInventIngredientCounts()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var recipe = f.Recipe(f.Ingredient(board, 2));
        var selection = Selection(recipe, f.Allocate(recipe, board));

        Assert.Empty(CraftingMenuTooltip.IngredientSummary(selection with { Allocation = null }));
        Assert.Empty(CraftingMenuTooltip.IngredientSummary(selection with
        {
            Evaluation = CraftingRecipeEvaluation.Unsupported("unsupported")
        }));
    }

    private static CraftingRecipeSelection Selection(GridRecipe recipe, CraftingAllocation allocation)
    {
        var entry = new CraftingRecipeEntry(0, recipe, "Output");
        return new(new(0, [entry]), entry, allocation.Evaluation, false, allocation);
    }

    private static ItemStack NamedStack(AllocationFixture f, int id, string name, int quantity, int maxStack = 64)
    {
        var stack = f.Stack(id, name, quantity, maxStack);
        var item = new Mock<MockItem>(id, (byte)0, f.Api.Object) { CallBase = true };
        item.Object.Code = stack.Item.Code;
        item.Object.MaxStackSize = maxStack;
        item.Setup(value => value.GetHeldItemName(It.IsAny<ItemStack>())).Returns(name);
        return new(item.Object, quantity);
    }
}
