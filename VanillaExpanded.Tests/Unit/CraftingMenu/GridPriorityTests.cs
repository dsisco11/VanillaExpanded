using Moq;
using VanillaExpanded.CraftingMenu;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public sealed class GridPriorityTests
{
    [Fact]
    public void AvailabilityPromotesMoreGridKindsBeforeCraftabilityAndNameIgnoresThem()
    {
        var f = new AllocationFixture();
        var wood = f.Stack(1, "wood", 1);
        var stick = f.Stack(2, "stick", 1);
        var stone = f.Stack(3, "stone", 1);
        var none = Named(f.Recipe(f.Ingredient(stone)), "A unrelated");
        var one = Named(f.Recipe(f.Ingredient(wood, 4)), "B one kind");
        var both = Named(f.Recipe(f.Ingredient(wood, 4), f.Ingredient(stick, 4)), "C both kinds");
        var unsupported = Named(f.Recipe(f.Ingredient(wood), f.Ingredient(stick)), "D unsupported");
        unsupported.CopyAttributesFrom = "I";
        f.Register(none, one, both, unsupported);
        f.Snapshot(stone);
        f.Grid[8].Itemstack = wood;
        f.Grid[6].Itemstack = stick;
        int captures = 0;
        using var session = new CraftingMenuSession(f.Catalogue.Build(none, one, both, unsupported), f.Allocator,
            () =>
            {
                captures++;
                return CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true);
            }, (_, _) => throw new InvalidOperationException("Sorting must not dispatch."));
        session.Refresh();
        Assert.Equal(new[] { both, one, none, unsupported }, session.Rows.Select(row => row.Recipe.Recipe));
        Assert.Equal(new[] { 2, 1, 0, 0 }, session.Rows.Select(row => row.GridMatches));
        Assert.False(session.Rows[0].Evaluation.CanCraft);
        Assert.True(session.Rows[2].Evaluation.CanCraft);
        Assert.False(session.Rows[3].Evaluation.IsSupported);
        var allocations = session.Rows.ToDictionary(row => row.Output.Id, row => row.Allocation);
        session.Sort.Select("name");
        Assert.Equal(new[] { none, one, both, unsupported }, session.Rows.Select(row => row.Recipe.Recipe));
        session.Sort.Select("availability");
        Assert.Equal(new[] { both, one, none, unsupported }, session.Rows.Select(row => row.Recipe.Recipe));
        Assert.All(session.Rows, row => Assert.Same(allocations[row.Output.Id], row.Allocation));
        Assert.Equal(1, captures);
        f.Grid[8].Itemstack = null;
        f.Grid[6].Itemstack = null;
        session.Refresh();
        Assert.All(session.Rows, row => Assert.Equal(0, row.GridMatches));
        Assert.Equal(new[] { none, one, both, unsupported }, session.Rows.Select(row => row.Recipe.Recipe));
        Assert.Equal(2, captures);
    }

    [Fact]
    public void AutoUsesActiveOrderingBeforeFiltersWhilePinsKeepTheirOwnRelevanceAndAllocation()
    {
        var f = new AllocationFixture();
        var wood = f.Stack(1, "wood", 1);
        var stone = f.Stack(2, "stone", 1);
        var carried = f.Recipe(f.Ingredient(stone));
        var related = f.Recipe(f.Ingredient(wood, 4));
        related.Output = carried.Output!.Clone();
        f.Register(carried, related);
        f.Snapshot(stone);
        f.Grid[8].Itemstack = wood;
        using var session = Session(f, carried, related);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.Same(related, row.Recipe.Recipe);
        Assert.Equal(1, row.GridMatches);
        Assert.False(row.Evaluation.CanCraft);
        Assert.Equal(1, Assert.Single(row.Allocation!.Ordinary).Quantity);
        Assert.Equal(4, Assert.Single(row.Allocation.Requirements).Quantity);
        session.Select(row.Output.Id);
        session.Interact(row.Output.Id);
        session.ChooseAlternative(row.Output.Alternatives.Single(entry => entry.Recipe == carried).Id);
        row = Assert.Single(session.Rows);
        Assert.True(row.IsExplicit);
        Assert.Same(carried, row.Recipe.Recipe);
        Assert.Equal(0, row.GridMatches);
        session.CraftableOnly.SetEnabled(true);
        Assert.True(Assert.Single(session.Rows).Evaluation.CanCraft);
        session.Interact(row.Output.Id);
        session.Auto();
        Assert.Empty(session.Rows);
        Assert.Equal(CraftingMenuEmptyState.NoCraftableRecipes, session.EmptyState);
        session.Search("no-match");
        Assert.Equal(CraftingMenuEmptyState.NoSearchMatches, session.EmptyState);
        session.Search("");
        session.Sort.Select("name");
        Assert.Same(carried, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.Equal(CraftingMenuEmptyState.None, session.EmptyState);
        session.Interact(row.Output.Id);
        session.ChooseAlternative(row.Output.Alternatives.Single(entry => entry.Recipe == related).Id);
        Assert.Empty(session.Rows);
        session.CraftableOnly.SetEnabled(false);
        row = Assert.Single(session.Rows);
        Assert.True(row.IsExplicit);
        Assert.Same(related, row.Recipe.Recipe);
        session.Sort.Select("availability");
        Assert.Same(related, Assert.Single(session.Rows).Recipe.Recipe);
        session.Sort.Select("name");
        Assert.Same(related, Assert.Single(session.Rows).Recipe.Recipe);
        session.Interact(row.Output.Id);
        session.Auto();
        Assert.Same(carried, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.False(Assert.Single(session.Rows).IsExplicit);
    }

    [Fact]
    public void LiveGridChangesDeferRowOrderingAndAutomaticAlternativeChangesUntilInteractionEnds()
    {
        var f = new AllocationFixture();
        var wood = f.Stack(1, "wood", 1);
        var stone = f.Stack(2, "stone", 1);
        var carried = Named(f.Recipe(f.Ingredient(stone)), "A carried");
        var related = f.Recipe(f.Ingredient(wood, 4));
        related.Output = carried.Output!.Clone();
        var other = Named(f.Recipe(f.Ingredient(wood, 4)), "B other");
        f.Register(carried, related, other);
        f.Snapshot(stone);
        var dispatched = new List<CraftingRecipeEntry>();
        using var session = new CraftingMenuSession(f.Catalogue.Build(carried, related, other), f.Allocator,
            () => CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            (entry, _) =>
            {
                dispatched.Add(entry);
                return new(CraftingDispatchStatus.Rejected, "test-only", 0);
            });
        session.Refresh();
        var shown = session.Rows[0];
        Assert.Same(carried, shown.Recipe.Recipe);
        session.Interact(shown.Output.Id);
        f.Grid[8].Itemstack = wood;
        session.Refresh();
        Assert.Equal(shown.Output.Id, session.Rows[0].Output.Id);
        Assert.Same(carried, session.Rows[0].Recipe.Recipe);
        Assert.Equal(0, session.Rows[0].GridMatches);
        Assert.Equal(1, session.Rows[1].GridMatches);
        session.Activate(shown.Output.Id, false);
        Assert.Same(carried, Assert.Single(dispatched).Recipe);
        session.Interact(null);
        Assert.Same(related, session.Rows[0].Recipe.Recipe);
        Assert.Equal(1, session.Rows[0].GridMatches);
        Assert.False(session.Rows[0].Evaluation.CanCraft);
    }

    [Fact]
    public void GridPredicateFailureKeepsThePublishedRowsAndTheNextRefreshCanRecover()
    {
        var f = new AllocationFixture();
        var item = new Mock<MockItem>(1, (byte)0, f.Api.Object) { CallBase = true };
        item.Object.Code = new AssetLocation("game:wood");
        item.Setup(value => value.MatchesForCrafting(It.IsAny<ItemStack>(), It.IsAny<IRecipeBase>(),
                It.IsAny<IRecipeIngredient>()))
            .Returns((ItemStack stack, IRecipeBase _, IRecipeIngredient _) =>
            {
                if (stack.TempAttributes.GetBool("fail")) throw new InvalidOperationException("Grid predicate failed.");
                return true;
            });
        var stock = new ItemStack(item.Object);
        var recipe = f.Recipe(f.Ingredient(stock, 4));
        f.Register(recipe);
        f.Snapshot(stock);
        using var session = Session(f, recipe);
        session.Refresh();
        var previous = Assert.Single(session.Rows);
        Assert.Equal(0, previous.GridMatches);
        var grid = CraftingStackSnapshot.Copy(stock);
        grid.TempAttributes.SetBool("fail", true);
        f.Grid[8].Itemstack = grid;
        var error = Assert.Throws<InvalidOperationException>(session.Refresh);
        Assert.Equal("Grid predicate failed.", error.Message);
        Assert.Same(previous, Assert.Single(session.Rows));
        grid.TempAttributes.SetBool("fail", false);
        session.Refresh();
        Assert.NotSame(previous, Assert.Single(session.Rows));
        Assert.Equal(1, Assert.Single(session.Rows).GridMatches);
    }

    [Fact]
    public void GridRelevanceDoesNotChangeCategoryOrSearchMembership()
    {
        var f = new AllocationFixture();
        var wood = f.Stack(1, "wood", 1);
        var tool = Named(f.Recipe(f.Ingredient(wood, 4)), "Matching tool");
        tool.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var block = Named(f.Recipe(f.Ingredient(wood, 4)), "Matching block");
        block.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["construction"];
        f.Register(tool, block);
        f.Grid[0].Itemstack = wood;
        using var session = Session(f, tool, block);
        session.Refresh();
        Assert.Equal(2, session.Rows.Length);
        session.SelectCategory("tools");
        Assert.Same(tool, Assert.Single(session.Rows).Recipe.Recipe);
        session.Search("block");
        Assert.Empty(session.Rows);
        session.SelectCategory("construction");
        Assert.Same(block, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.Equal(1, Assert.Single(session.Rows).GridMatches);
    }

    private static GridRecipe Named(GridRecipe recipe, string name)
    {
        recipe.Output!.ResolvedItemStack!.Attributes.SetString("displayName", name);
        return recipe;
    }

    private static CraftingMenuSession Session(AllocationFixture f, params GridRecipe[] recipes) =>
        new(f.Catalogue.Build(recipes), f.Allocator,
            () => CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            (_, _) => throw new InvalidOperationException("Ranking must not dispatch."));
}
