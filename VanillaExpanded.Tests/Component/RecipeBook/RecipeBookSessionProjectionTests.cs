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
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookSessionProjectionTests
{
    [Fact]
    public void CriteriaChangesReuseAllocationsAndInventoryRefreshEvaluatesExactlyOnce()
    {
        // Arrange an evaluated session and distinguish its initial capture from later projections.
        var f = new AllocationFixture();
        var ingredient = f.Stack(1, "a", 1);
        var recipe = f.Recipe(f.Ingredient(ingredient));
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        f.Register(recipe);
        f.Snapshot(ingredient);
        int captures = 0;
        using var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator, () =>
        {
            captures++;
            return CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true);
        }, (_, _) => throw new InvalidOperationException("No dispatch is expected."));
        session.Refresh();
        var allocation = Assert.Single(session.Rows).Allocation;
        Assert.NotNull(allocation);
        Assert.True(allocation.Evaluation.CanCraft);
        int NativeChecks() => f.Events.Invocations.Count(call =>
            call.Method.Name == nameof(IEventAPI.TriggerMatchesRecipe) && call.Arguments.Count == 4);
        int validationsPerEvaluation = NativeChecks();

        // Act: change criteria repeatedly; these projections should reuse the existing evaluation.
        session.CraftableOnly.SetEnabled(true);
        session.Sort.Select("name");
        session.Search(recipe.Output.Code!.ToString());
        session.SelectCategory("tools");
        Assert.Same(allocation, Assert.Single(session.Rows).Allocation);
        session.Search("missing");
        Assert.Empty(session.Rows);
        session.CraftableOnly.SetEnabled(false);
        Assert.Empty(session.Rows);
        session.CraftableOnly.SetEnabled(true);
        session.Search("");
        session.SelectCategory(null);
        session.Sort.Select("availability");

        // Assert criteria changes neither capture inventory nor replace the row's allocation.
        Assert.Same(allocation, Assert.Single(session.Rows).Allocation);
        Assert.Equal(1, captures);
        Assert.Equal(validationsPerEvaluation, NativeChecks());

        // Act: refresh inventory, the separate transition that captures and evaluates again.
        session.Refresh();

        // Assert one fresh capture and allocation were published.
        Assert.Equal(2, captures);
        Assert.Equal(2 * validationsPerEvaluation, NativeChecks());
        Assert.NotSame(allocation, Assert.Single(session.Rows).Allocation);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CraftableSearchAndCategoryComposeIndependently(bool searching, bool categorized, bool craftable)
    {
        var f = new AllocationFixture();
        var stock = Enumerable.Range(0, 8).Select(i => f.Stack(i + 1, "ingredient-" + i, (i & 4) != 0 ? 2 : 1)).ToArray();
        var recipes = stock.Select((stack, i) =>
        {
            var recipe = f.Recipe(f.Ingredient(stack, 2));
            var output = f.Stack(100 + i, ((i & 2) != 0 ? "shared-" : "other-") + i, 1);
            output.Collectible.CreativeInventoryTabs = [(i & 1) != 0 ? "tools" : "construction"];
            recipe.Output = f.Ingredient(output);
            return recipe;
        }).ToArray();
        f.Register(recipes);
        f.Snapshot(stock);
        using var session = Session(f, recipes);
        session.Refresh();
        session.Search(searching ? "shared" : "");
        session.SelectCategory(categorized ? "tools" : null);
        session.CraftableOnly.SetEnabled(craftable);
        AssertRows(searching, categorized, craftable);
        session.Search("");
        AssertRows(false, categorized, craftable);
        session.Search(searching ? "shared" : "");
        session.SelectCategory(null);
        AssertRows(searching, false, craftable);
        session.SelectCategory(categorized ? "tools" : null);
        session.CraftableOnly.SetEnabled(false);
        AssertRows(searching, categorized, false);

        void AssertRows(bool search, bool category, bool available)
        {
            var expected = recipes.Where((_, i) => (!search || (i & 2) != 0)
                && (!category || (i & 1) != 0) && (!available || (i & 4) != 0));
            Assert.Equal(expected.OrderBy(recipe => recipe.Name!.ToString()),
                session.Rows.Select(row => row.Recipe.Recipe).OrderBy(recipe => recipe.Name!.ToString()));
        }
    }

    [Fact]
    public void CraftableMembershipTracksActualIngredientThresholdsWithoutCriteriaReallocation()
    {
        var f = new AllocationFixture();
        var ingredient = f.Stack(1, "a", 1);
        var recipe = f.Recipe(f.Ingredient(ingredient, 2));
        f.Register(recipe);
        using var session = Session(f, recipe);
        session.CraftableOnly.SetEnabled(true);
        foreach (int quantity in new[] { 0, 1, 2, 1, 0, 3 })
        {
            var stack = ingredient.Clone();
            stack.StackSize = quantity;
            f.Snapshot(quantity == 0 ? [] : [stack]);
            session.Refresh();
            Assert.Equal(quantity >= 2 ? 1 : 0, session.Rows.Length);
            session.CraftableOnly.SetEnabled(false);
            var row = Assert.Single(session.Rows);
            Assert.Equal(Math.Min(quantity, 2), row.Evaluation.Allocated);
            Assert.Equal(quantity >= 2, row.Evaluation.CanCraft);
            session.CraftableOnly.SetEnabled(true);
            if (quantity >= 2) Assert.Same(row.Allocation, Assert.Single(session.Rows).Allocation);
        }
    }

    [Fact]
    public void CraftableOnlyHidesUnavailablePinUntilDisabledAndAutoRestoresAvailableVariant()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        first.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var second = f.Recipe(f.Ingredient(b));
        second.Output = first.Output;
        f.Register(first, second);
        f.Snapshot(a);
        using var session = Session(f, first, second);
        session.Refresh();
        var original = Assert.Single(session.Rows);
        var unavailable = original.Output.Alternatives.Single(row => row.Id != original.Recipe.Id);
        session.Select(original.Output.Id);
        session.ChooseAlternative(unavailable.Id);
        session.CraftableOnly.SetEnabled(true);
        Assert.Empty(session.Rows);
        session.Auto();
        session.Refresh();
        Assert.Empty(session.Rows);
        session.CraftableOnly.SetEnabled(false);
        Assert.Equal(unavailable.Id, Assert.Single(session.Rows).Recipe.Id);
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        session.Auto();
        Assert.Equal(original.Recipe.Id, Assert.Single(session.Rows).Recipe.Id);
        session.CraftableOnly.SetEnabled(true);
        Assert.Single(session.Rows);
        session.ChooseAlternative(unavailable.Id);
        Assert.Empty(session.Rows);
        session.SelectCategory("tools");
        Assert.Null(session.SelectedOutput);
        Assert.Equal(original.Recipe.Id, Assert.Single(session.Rows).Recipe.Id);
        Assert.False(Assert.Single(session.Rows).IsExplicit);
    }

    [Theory]
    [InlineData("replace-search")]
    [InlineData("clear-search")]
    [InlineData("replace-category")]
    [InlineData("clear-category")]
    public void FailedCriteriaMutationDoesNotPublishOrChangeExistingCriteria(string operation)
    {
        var f = new AllocationFixture();
        var tool = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        tool.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var block = f.Recipe(f.Ingredient(f.Stack(2, "b", 1)));
        block.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["construction"];
        f.Register(tool, block);
        using var session = Session(f, tool, block);
        session.Refresh();
        session.Search(tool.Output.Code!.ToString());
        session.SelectCategory("tools");
        var previous = session.Rows;
        int notifications = 0;
        session.Changed += () => notifications++;
        using var mutation = session.Filters.Register("test:mutation", new CallbackFilter(() =>
        {
            switch (operation)
            {
                case "replace-search": session.Search(block.Output.Code!.ToString()); break;
                case "clear-search": session.Search(""); break;
                case "replace-category": session.SelectCategory("construction"); break;
                case "clear-category": session.SelectCategory(null); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }));
        Assert.Throws<InvalidOperationException>(session.Refresh);
        Assert.True(previous == session.Rows);
        Assert.Equal(0, notifications);
        Assert.Equal("tools", session.Category);
        mutation.Dispose();
        session.Refresh();
        Assert.Same(tool, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ReconciliationRetainsEligibleRowsAndAppendsNewGroupsOnceInCanonicalOrder()
    {
        var f = new AllocationFixture();
        var ingredients = Enumerable.Range(1, 4).Select(id => f.Stack(id, "ingredient-" + id, 1)).ToArray();
        var recipes = ingredients.Select(stack => f.Recipe(f.Ingredient(stack))).ToArray();
        f.Register(recipes);
        f.Snapshot(ingredients[0]);
        using var session = Session(f, recipes);
        session.Refresh();
        var original = session.Rows.Take(2).ToArray();
        using var filter = session.Filters.Register("test:membership",
            new PredicateFilter(row => original.Any(previous => previous.Output.Id == row.Output.Id)));
        session.Refresh();
        session.Interact(original[0].Output.Id);
        filter.Replace(new PredicateFilter(_ => true));
        f.Snapshot(ingredients[1], ingredients[2], ingredients[3]);
        session.Refresh();
        Assert.Equal(original.Select(row => row.Output.Id), session.Rows.Take(2).Select(row => row.Output.Id));
        Assert.False(session.Rows[0].Evaluation.CanCraft);
        Assert.Equal(1, Assert.Single(session.Rows[0].Evaluation.Missing).Quantity);
        Assert.All(session.Rows, row => Assert.Same(row.Evaluation, row.Allocation!.Evaluation));
        var displayed = session.Rows;
        session.Refresh();
        Assert.Equal(displayed.Select(row => row.Recipe.Id), session.Rows.Select(row => row.Recipe.Id));
        Assert.Equal(4, session.Rows.Select(row => row.Output.Id).Distinct().Count());
        session.Interact(null);
        Assert.NotEqual(original[0].Output.Id, session.Rows[0].Output.Id);
        Assert.Equal(original[0].Output.Id, session.Rows[^1].Output.Id);
        Assert.Equal(session.Rows.Take(3).Select(row => row.Output.Id),
            displayed.Skip(1).Select(row => row.Output.Id));
    }

    [Fact]
    public void InventoryFilterReplacesIneligibleAutomaticRecipeButNeverReplacesOrUnpinsHiddenPin()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        second.Output = first.Output;
        f.Register(first, second);
        f.Snapshot(a);
        using var session = Session(f, first, second);
        using var filter = session.Filters.Register("test:inventory", new PredicateFilter(row => row.Evaluation.CanCraft));
        session.Refresh();
        var original = Assert.Single(session.Rows);
        session.Interact(original.Output.Id);
        f.Snapshot(b);
        session.Refresh();
        var replacement = Assert.Single(session.Rows);
        Assert.Equal(original.Output.Id, replacement.Output.Id);
        Assert.Same(second, replacement.Recipe.Recipe);
        session.Select(original.Output.Id);
        session.ChooseAlternative(replacement.Recipe.Id);
        session.Interact(original.Output.Id);
        f.Snapshot(a);
        session.Refresh();
        Assert.Empty(session.Rows);
        session.Auto();
        session.ChooseAlternative(original.Recipe.Id);
        session.Refresh();
        Assert.Empty(session.Rows);
        f.Snapshot(b);
        session.Refresh();
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(second, Assert.Single(session.Rows).Recipe.Recipe);
    }

    [Fact]
    public void ExplicitAlternativeAndAutoOverrideEligibleFrozenAlternativeImmediately()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        second.Output = first.Output;
        f.Register(first, second);
        f.Snapshot(a);
        using var session = Session(f, first, second);
        using var filter = session.Filters.Register("test:all", new PredicateFilter(_ => true));
        session.Refresh();
        var original = Assert.Single(session.Rows);
        session.Select(original.Output.Id);
        session.Interact(original.Output.Id);
        var other = original.Output.Alternatives.Single(recipe => recipe.Id != original.Recipe.Id);
        session.ChooseAlternative(other.Id);
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Equal(other.Id, Assert.Single(session.Rows).Recipe.Id);
        session.Auto();
        Assert.False(Assert.Single(session.Rows).IsExplicit);
        Assert.Equal(original.Recipe.Id, Assert.Single(session.Rows).Recipe.Id);
    }

    [Fact]
    public void FailureWhileFilteringStableRecipesDoesNotPublishPartialRows()
    {
        // Arrange a retained displayed row before a refresh makes its evaluation fail.
        var f = new AllocationFixture();
        var retainedIngredient = f.Stack(1, "a", 1);
        var canonicalIngredient = f.Stack(2, "b", 1);
        var retainedRecipe = f.Recipe(f.Ingredient(retainedIngredient));
        var canonicalRecipe = f.Recipe(f.Ingredient(canonicalIngredient));
        canonicalRecipe.Output = retainedRecipe.Output!.Clone();
        f.Register(retainedRecipe, canonicalRecipe);
        f.Snapshot(retainedIngredient);
        using var session = Session(f, retainedRecipe, canonicalRecipe);
        session.Refresh();
        var retained = Assert.Single(session.Rows);
        Assert.Same(retainedRecipe, retained.Recipe.Recipe);
        Assert.True(retained.Evaluation.CanCraft);
        session.Interact(retained.Output.Id);
        var previous = session.Rows;
        f.Snapshot(canonicalIngredient);
        int notifications = 0;
        session.Changed += () => notifications++;

        // Act: fail projection for the retained alternative during the inventory refresh.
        using var filter = session.Filters.Register("test:failure", new PredicateFilter(selection =>
            ReferenceEquals(selection.Recipe.Recipe, retainedRecipe) && !selection.Evaluation.CanCraft
                ? throw new InvalidOperationException("Retained alternative failed during projection.")
                : true));
        Assert.Throws<InvalidOperationException>(session.Refresh);

        // Assert failure preserves the previous publication and emits no change notification.
        Assert.True(previous == session.Rows);
        Assert.True(Assert.Single(session.Rows).Evaluation.CanCraft);
        Assert.Equal(0, notifications);

        // Act: recover by replacing the failing predicate, then end the interaction freeze.
        filter.Replace(new PredicateFilter(_ => true));
        session.Refresh();

        // Assert the recovered refresh publishes once before canonical interaction ordering resumes.
        Assert.Equal(1, notifications);
        var refreshedRetained = Assert.Single(session.Rows);
        Assert.Same(retainedRecipe, refreshedRetained.Recipe.Recipe);
        Assert.False(refreshedRetained.Evaluation.CanCraft);
        session.Interact(null);

        // Assert ending interaction selects the newly eligible canonical alternative.
        Assert.Same(canonicalRecipe, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.True(Assert.Single(session.Rows).Evaluation.CanCraft);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void SortChangesEndInteractionWithoutChangingSelectionPinsOrAllocations()
    {
        var f = new AllocationFixture();
        var wood = f.Stack(1, "wood", 1);
        var stone = f.Stack(2, "stone", 1);
        var alpha = f.Recipe(f.Ingredient(wood));
        alpha.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "Alpha");
        var alternate = f.Recipe(f.Ingredient(wood, 2));
        alternate.Output = alpha.Output.Clone();
        var zulu = f.Recipe(f.Ingredient(stone));
        zulu.Output!.ResolvedItemStack!.Attributes.SetString("displayName", "Zulu");
        f.Register(alpha, alternate, zulu);
        f.Snapshot(stone);
        using var session = Session(f, alpha, alternate, zulu);
        session.Refresh();
        Assert.Equal(new[] { "Zulu", "Alpha" }, session.Rows.Select(row => row.Recipe.DisplayName));
        var row = session.Rows.Single(row => row.Recipe.Recipe == alpha);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Output.Alternatives.Single(entry => entry.Recipe == alternate).Id);
        session.Interact(row.Output.Id);
        var allocations = session.Rows.ToDictionary(selection => selection.Output.Id, selection => selection.Allocation);
        session.Sort.Select("name");
        Assert.Equal(new[] { "Alpha", "Zulu" }, session.Rows.Select(selection => selection.Recipe.DisplayName));
        Assert.Equal(row.Output.Id, session.SelectedOutput);
        Assert.True(session.Rows[0].IsExplicit);
        Assert.Same(alternate, session.Rows[0].Recipe.Recipe);
        Assert.All(session.Rows, selection => Assert.Same(allocations[selection.Output.Id], selection.Allocation));
        session.Sort.Select("availability");
        Assert.Equal(new[] { "Zulu", "Alpha" }, session.Rows.Select(selection => selection.Recipe.DisplayName));
        session.Sort.Select("name");
        f.Snapshot(wood);
        session.Refresh();
        Assert.Equal(new[] { "Alpha", "Zulu" }, session.Rows.Select(selection => selection.Recipe.DisplayName));
        Assert.True(session.Rows[0].IsExplicit);
        Assert.Same(alternate, session.Rows[0].Recipe.Recipe);
        session.Search(alpha.Output.Code!.ToString());
        Assert.Equal("Alpha", Assert.Single(session.Rows).Recipe.DisplayName);
        Assert.Equal("name", session.Sort.Selected.Id);
        session.CraftableOnly.SetEnabled(true);
        Assert.Empty(session.Rows);
        session.Sort.Select("availability");
        Assert.Empty(session.Rows);
        session.CraftableOnly.SetEnabled(false);
        Assert.True(Assert.Single(session.Rows).IsExplicit);
    }

    [Fact]
    public void CategoryChangesKeepSearchAndClearInteractionAndHiddenSelectionAcrossRefresh()
    {
        var f = new AllocationFixture();
        var tool = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        tool.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var block = f.Recipe(f.Ingredient(f.Stack(2, "b", 1)));
        block.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["construction"];
        f.Register(tool, block);
        var session = Session(f, tool, block);
        session.Refresh();
        var toolRow = session.Rows.Single(row => row.Recipe.Recipe == tool);
        session.Select(toolRow.Output.Id);
        session.ChooseAlternative(toolRow.Recipe.Id);
        session.Interact(toolRow.Output.Id);
        session.SelectCategory("construction");

        Assert.Null(session.SelectedOutput);
        Assert.Same(block, Assert.Single(session.Rows).Recipe.Recipe);
        session.Refresh();
        Assert.Same(block, Assert.Single(session.Rows).Recipe.Recipe);
        session.Search(tool.Output.Code!.ToString());
        Assert.Empty(session.Rows);
        session.SelectCategory("tools");
        Assert.Same(tool, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.False(Assert.Single(session.Rows).IsExplicit);
        session.Refresh();
        Assert.Single(session.Rows);
        Assert.Equal("tools", session.Category);
        Assert.Throws<ArgumentException>(() => session.SelectCategory("missing"));
        Assert.Equal("tools", session.Category);
        session.SelectCategory(null);
        Assert.Single(session.Rows);
        session.Search("");
        Assert.Equal(2, session.Rows.Length);
    }

    [Fact]
    public void HoverFreezesAutomaticAlternativeUntilPointerLeavesAndPinsSurviveRefresh()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        second.Output = first.Output;
        f.Register(first, second);
        f.Snapshot(a);
        var session = Session(f, first, second);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        session.Interact(row.Output.Id);
        f.Snapshot(b);
        session.Refresh();
        Assert.Same(first, Assert.Single(session.Rows).Recipe.Recipe);
        Assert.False(Assert.Single(session.Rows).Evaluation.CanCraft);
        session.Interact(null);
        Assert.Same(second, Assert.Single(session.Rows).Recipe.Recipe);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        session.Refresh();
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(first, Assert.Single(session.Rows).Recipe.Recipe);
        session.Auto();
        Assert.False(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(second, Assert.Single(session.Rows).Recipe.Recipe);
    }

    [Fact]
    public void UnsupportedAlternativesRemainInspectableAndCannotDispatchEitherMode()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        recipe.CopyAttributesFrom = "I";
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(row.Recipe.Id);
        Assert.Null(row.Evaluation.Relevance);
        Assert.Empty(row.Evaluation.Missing);
        Assert.Equal(CraftingDispatchStatus.Rejected, session.Activate(row.Output.Id, false).Status);
        Assert.Equal(CraftingDispatchStatus.Rejected, session.Activate(row.Output.Id, true).Status);
        string text = RecipeBookTooltip.Warnings(row, key => key);
        Assert.Contains(row.Evaluation.UnsupportedReason!, text);
        Assert.DoesNotContain("recipebook-missing", text);
    }

    [Fact]
    public void SearchRetainsCatalogueIncludingZeroStockAndReportsEmptyResults()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "a", 1)));
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        Assert.Equal(0, row.Evaluation.Allocated);
        session.Search("  " + row.Recipe.DisplayName.ToUpperInvariant() + " ");
        Assert.Single(session.Rows);
        session.Search("nothing-matches-this");
        Assert.Empty(session.Rows);
        session.Search(" ");
        Assert.Single(session.Rows);
    }
}
