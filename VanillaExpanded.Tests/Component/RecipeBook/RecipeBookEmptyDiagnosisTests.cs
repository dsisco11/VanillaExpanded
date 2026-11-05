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
public sealed class RecipeBookEmptyDiagnosisTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EmptyDiagnosisChecksSearchBeforeCraftabilityAndReusesAllocations(bool craftable, bool searching)
    {
        var f = new AllocationFixture();
        var stock = f.Stack(1, "ingredient", 1);
        var recipe = f.Recipe(f.Ingredient(stock));
        recipe.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        f.Register(recipe);
        f.Snapshot();
        int captures = 0;
        using var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator, () =>
        {
            captures++;
            return CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true);
        }, (_, _) => throw new InvalidOperationException("No dispatch is expected."));
        session.Refresh();
        var allocation = Assert.Single(session.Rows).Allocation;
        session.SelectCategory("tools");
        session.Search(searching ? "output" : " \t ");
        session.CraftableOnly.SetEnabled(craftable);
        Assert.Equal(craftable
            ? searching ? RecipeBookEmptyState.NoCraftableSearchMatches : RecipeBookEmptyState.NoCraftableRecipes
            : RecipeBookEmptyState.None, session.EmptyState);
        session.Search("no-such-output");
        Assert.Empty(session.Rows);
        Assert.Equal(RecipeBookEmptyState.NoSearchMatches, session.EmptyState);
        session.Search(searching ? "output" : "");
        session.CraftableOnly.SetEnabled(false);
        Assert.Same(allocation, Assert.Single(session.Rows).Allocation);
        Assert.Equal(RecipeBookEmptyState.None, session.EmptyState);
        Assert.Equal("tools", session.Category);
        Assert.Equal(1, captures);
        session.CraftableOnly.SetEnabled(true);
        f.Snapshot(stock);
        session.Refresh();
        Assert.Single(session.Rows);
        Assert.Equal(RecipeBookEmptyState.None, session.EmptyState);
        f.Snapshot();
        session.Refresh();
        Assert.Empty(session.Rows);
        Assert.Equal(searching ? RecipeBookEmptyState.NoCraftableSearchMatches
            : RecipeBookEmptyState.NoCraftableRecipes, session.EmptyState);
        Assert.Equal(3, captures);
    }

    [Fact]
    public void EmptyCatalogueHasNoMisleadingFilterDiagnosis()
    {
        var f = new AllocationFixture();
        using var session = new RecipeBookSession(f.Catalogue.Build(), f.Allocator,
            () => throw new InvalidOperationException("No capture is expected."),
            (_, _) => throw new InvalidOperationException("No dispatch is expected."));
        session.Search("anything");
        session.CraftableOnly.SetEnabled(true);
        Assert.Empty(session.Rows);
        Assert.Equal(RecipeBookEmptyState.NoRecipes, session.EmptyState);
    }

    [Fact]
    public void EmptyDiagnosisDoesNotCountSearchMatchesOutsideTheSelectedCategory()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var tools = f.Recipe(f.Ingredient(a));
        tools.Output = f.Ingredient(f.Stack(100, "tool-output", 1));
        tools.Output.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var construction = f.Recipe(f.Ingredient(b));
        construction.Output = f.Ingredient(f.Stack(101, "construction-output", 1));
        construction.Output.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["construction"];
        f.Register(tools, construction);
        f.Snapshot(b);
        using var session = Session(f, tools, construction);
        session.Refresh();
        session.SelectCategory("tools");
        session.CraftableOnly.SetEnabled(true);
        Assert.Equal(RecipeBookEmptyState.NoCraftableRecipes, session.EmptyState);
        session.Search("construction");
        Assert.Equal(RecipeBookEmptyState.NoSearchMatches, session.EmptyState);
        session.Search("tool-output");
        Assert.Equal(RecipeBookEmptyState.NoCraftableSearchMatches, session.EmptyState);
    }

    [Fact]
    public void DiagnosticProviderFailureDoesNotPublishAnEmptySuccessState()
    {
        var f = new AllocationFixture();
        var stock = f.Stack(1, "a", 1);
        var recipe = f.Recipe(f.Ingredient(stock));
        f.Register(recipe);
        f.Snapshot(stock);
        using var session = Session(f, recipe);
        session.CraftableOnly.SetEnabled(true);
        session.Refresh();
        var previous = Assert.Single(session.Rows);
        var failure = new InvalidOperationException("Diagnostic provider failed.");
        using var registration = session.Filters.Register("test:failure", new PredicateFilter(_ => throw failure));
        f.Snapshot();
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(session.Refresh));
        Assert.Same(previous, Assert.Single(session.Rows));
        Assert.Equal(RecipeBookEmptyState.None, session.EmptyState);
        registration.Dispose();
        session.Refresh();
        Assert.Empty(session.Rows);
        Assert.Equal(RecipeBookEmptyState.NoCraftableRecipes, session.EmptyState);
    }

    [Fact]
    public void EmptyDiagnosisRespectsOtherProvidersAndTheExactPinnedAlternative()
    {
        var f = new AllocationFixture();
        var a = f.Stack(1, "a", 1);
        var b = f.Stack(2, "b", 1);
        var first = f.Recipe(f.Ingredient(a));
        var second = f.Recipe(f.Ingredient(b));
        second.Output = first.Output!.Clone();
        f.Register(first, second);
        f.Snapshot(a);
        using var session = Session(f, first, second);
        session.Refresh();
        var row = Assert.Single(session.Rows);
        session.Select(row.Output.Id);
        session.ChooseAlternative(session.Rows[0].Output.Alternatives.Single(entry => entry.Recipe == second).Id);
        session.Search("output");
        session.CraftableOnly.SetEnabled(true);
        Assert.Empty(session.Rows);
        Assert.Equal(RecipeBookEmptyState.NoCraftableSearchMatches, session.EmptyState);
        using var hidden = session.Filters.Register("test:hidden", new PredicateFilter(_ => false));
        session.Refresh();
        Assert.Equal(RecipeBookEmptyState.NoSearchMatches, session.EmptyState);
        hidden.Dispose();
        session.Refresh();
        Assert.Equal(RecipeBookEmptyState.NoCraftableSearchMatches, session.EmptyState);
        session.CraftableOnly.SetEnabled(false);
        Assert.True(Assert.Single(session.Rows).IsExplicit);
        Assert.Same(second, Assert.Single(session.Rows).Recipe.Recipe);
    }
}
