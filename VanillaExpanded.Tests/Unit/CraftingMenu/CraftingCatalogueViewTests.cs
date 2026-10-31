using System.Collections.Immutable;
using VanillaExpanded.CraftingMenu;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public class CraftingCatalogueViewTests
{
    [Fact]
    public void DefaultOrderingRetainsEveryCriterionAndTheStableIdentityTieBreaker()
    {
        var fixture = new CatalogueFixture();
        var data = new[] {
            (Name: "unsupported", Label: "A", Evaluation: CraftingRecipeEvaluation.Unsupported("mod:unsupported")),
            (Name: "low", Label: "A", Evaluation: Partial(1, 4)),
            (Name: "missing-two", Label: "A", Evaluation: Partial(2, 4)),
            (Name: "name-z", Label: "Z", Evaluation: Partial(1, 2)),
            (Name: "name-a", Label: "A", Evaluation: Partial(1, 2)),
            (Name: "stable-z", Label: "SAME", Evaluation: Partial(1, 2)),
            (Name: "stable-a", Label: "same", Evaluation: Partial(1, 2)),
            (Name: "not-craftable-full", Label: "A", Evaluation:
                CraftingRecipeEvaluation.Supported(2, 2, false, ImmutableArray<CraftingMissingRequirement>.Empty)),
            (Name: "full", Label: "ZZZ", Evaluation: CatalogueFixture.Full())
        };
        var recipes = data.Select(item =>
        {
            var recipe = fixture.Recipe(item.Name);
            recipe.Output!.ResolvedItemStack!.Attributes.SetString("displayName", item.Label);
            return recipe;
        }).ToArray();
        var catalogue = fixture.Build(recipes);
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(catalogue.Recipes.ToDictionary(entry => entry.Id,
            entry => data.Single(item => item.Name == entry.Recipe.Name!.Path).Evaluation));

        Assert.Equal(new[] { "full", "not-craftable-full", "name-a", "stable-a", "stable-z", "name-z", "missing-two", "low", "unsupported" },
            Query(view).Select(row => row.Recipe.Recipe.Name!.Path));
    }

    [Fact]
    public void FiltersObserveTheResolvedPinnedOrAutomaticAlternative()
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "mod:board");
        var catalogue = fixture.Build(fixture.Recipe("a", output), fixture.Recipe("b", output));
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [catalogue.Recipes[0].Id] = CatalogueFixture.Full(),
            [catalogue.Recipes[1].Id] = Partial(1, 2)
        });
        using var filters = new RecipeFilterSet();
        var observed = new List<CraftingRecipeSelection>();
        filters.Register("test:selected", new ObservingFilter(observed));
        view.SelectAlternative(0, catalogue.Recipes[1].Id);
        var pinned = Assert.Single(view.GetRows(filters));
        Assert.Equal(catalogue.Recipes[1].Id, pinned.Recipe.Id);
        Assert.True(pinned.IsExplicit);
        Assert.Same(pinned, Assert.Single(observed));
        observed.Clear();
        view.SelectAuto(0);
        var automatic = Assert.Single(view.GetRows(filters));
        Assert.Equal(catalogue.Recipes[0].Id, automatic.Recipe.Id);
        Assert.False(automatic.IsExplicit);
        Assert.Same(automatic, Assert.Single(observed));
        Assert.Throws<ArgumentNullException>(() => view.GetRows(null!));
    }

    [Fact]
    public void CategoriesUseImmutableNativeTagsAndSearchWithinOverlappingGroups()
    {
        var fixture = new CatalogueFixture();
        var tool = fixture.Stack(10, "mod:hammer");
        tool.Collectible.CreativeInventoryTabs = ["general", "items", "tools", "tools"];
        var custom = fixture.Stack(11, "mod:bench");
        custom.Collectible.CreativeInventoryTabs = ["general", "mod:workshop"];
        var generic = fixture.Stack(12, "mod:uncategorized");
        generic.Collectible.CreativeInventoryTabs = ["general"];
        var catalogue = fixture.Build(fixture.Recipe("hammer", tool), fixture.Recipe("bench", custom),
            fixture.Recipe("uncategorized", generic), fixture.Recipe("no-tags"));
        tool.Collectible.CreativeInventoryTabs[2] = "changed";
        var view = new CraftingCatalogueView(catalogue);

        Assert.Equal(new[] { "", "items", "mod:workshop", "tools" }, catalogue.CategoryCodes);
        Assert.Equal(new[] { "items", "tools" }, catalogue.Recipes[0].CategoryCodes);
        Assert.Equal(4, Query(view).Length);
        Assert.Equal("hammer", Assert.Single(Query(view, "", "tools")).Recipe.Recipe.Name!.Path);
        Assert.Single(Query(view, "HAMMER", "items"));
        Assert.Empty(Query(view, "bench", "tools"));
        Assert.Single(Query(view, "bench", "mod:workshop"));
        Assert.Equal(2, Query(view, "", CraftingCatalogue.OtherCategory).Length);
        Assert.Empty(Query(view, "", "general"));
    }

    [Fact]
    public void CreativeStackCategoriesMatchOutputAttributesWithoutIncludingOtherVariants()
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "mod:container");
        output.Attributes.SetString("variant", "tool");
        var matching = output.Clone();
        var other = output.Clone();
        other.Attributes.SetString("variant", "decorative");
        output.Collectible.CreativeInventoryStacks = [
            new() { Tabs = ["general", "tools"], Stacks = [new JsonItemStack { ResolvedItemstack = matching }] },
            new() { Tabs = ["decorative"], Stacks = [new JsonItemStack { ResolvedItemstack = other }] }
        ];
        var catalogue = fixture.Build(fixture.Recipe("container", output));

        Assert.Equal(new[] { "tools" }, catalogue.CategoryCodes);
        Assert.Equal(new[] { "tools" }, Assert.Single(catalogue.Recipes).CategoryCodes);
        Assert.Empty(Query(new CraftingCatalogueView(catalogue), "", "decorative"));
    }

    [Fact]
    public void OnlyIncludedRecipeOutputsContributeCategories()
    {
        var fixture = new CatalogueFixture();
        var hidden = fixture.Recipe("hidden");
        hidden.Enabled = false;
        hidden.Output!.ResolvedItemStack!.Collectible.CreativeInventoryTabs = ["tools"];
        var unresolved = fixture.Recipe("unresolved");
        unresolved.Output!.ResolvedItemStack = null;
        var catalogue = fixture.Build(hidden, unresolved);

        Assert.Equal(new[] { CraftingCatalogue.OtherCategory }, catalogue.CategoryCodes);
        Assert.Single(Query(new CraftingCatalogueView(catalogue), "", CraftingCatalogue.OtherCategory));
        Assert.Empty(fixture.Build().CategoryCodes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ÉRABLE")]
    [InlineData("  planche\térable ")]
    [InlineData("mod:board")]
    [InlineData("érable board")]
    public void SearchNormalizesWhitespaceCaseAndMatchesLocalizedNameOrCode(string query)
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "mod:board");
        output.Attributes.SetString("displayName", "Planche d'érable");
        var view = new CraftingCatalogueView(fixture.Build(fixture.Recipe("localized", output)));

        Assert.Single(Query(view, query));
        Assert.Empty(Query(view, "érable iron"));
    }

    [Fact]
    public void SearchPreservesRelevanceOrderingAndReportsEmptyResults()
    {
        var fixture = new CatalogueFixture();
        var catalogue = fixture.Build(fixture.Recipe("low"), fixture.Recipe("high"));
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [catalogue.Recipes[0].Id] = Partial(1, 4),
            [catalogue.Recipes[1].Id] = Partial(3, 4)
        });

        Assert.Equal(new[] { "high", "low" }, Query(view, "output").Select(selection => selection.Recipe.Recipe.Name!.Path));
        Assert.Empty(Query(view, "no-such-output"));
    }

    [Fact]
    public void AutomaticSelectionUsesHighestSupportedRelevanceAndStableTies()
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "game:board");
        var catalogue = fixture.Build(fixture.Recipe("mod:z", output), fixture.Recipe("mod:a", output));
        var view = new CraftingCatalogueView(catalogue);
        var z = catalogue.Recipes[0]; var a = catalogue.Recipes[1];
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [z.Id] = Partial(1, 4), [a.Id] = Partial(3, 4)
        });
        Assert.Equal(a.Id, view.GetSelection(0).Recipe.Id);

        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [z.Id] = Partial(3, 4), [a.Id] = Partial(3, 4)
        });
        Assert.Equal(a.Id, view.GetSelection(0).Recipe.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitAlternativeSurvivesRefreshUntilAutoOrDeselection(bool deselect)
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "game:board");
        var catalogue = fixture.Build(fixture.Recipe("mod:a", output), fixture.Recipe("mod:b", output, 8));
        var view = new CraftingCatalogueView(catalogue);
        var a = catalogue.Recipes[0]; var b = catalogue.Recipes[1];
        view.SelectAlternative(0, b.Id);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [a.Id] = CatalogueFixture.Full(),
            [b.Id] = CraftingRecipeEvaluation.Unsupported("mod:unsupported")
        });

        var pinned = view.GetSelection(0);
        Assert.Equal(b.Id, pinned.Recipe.Id);
        Assert.True(pinned.IsExplicit);
        Assert.Equal(8, pinned.Recipe.OutputQuantity);
        Assert.False(pinned.CanPlace);
        Assert.Equal("mod:unsupported", pinned.Evaluation.UnsupportedReason);
        Assert.Null(pinned.Evaluation.Relevance);
        Assert.Empty(pinned.Evaluation.Missing);

        if (deselect) view.Deselect(0); else view.SelectAuto(0);
        Assert.Equal(a.Id, view.GetSelection(0).Recipe.Id);
        Assert.False(view.GetSelection(0).IsExplicit);
    }

    [Fact]
    public void AllUnsupportedAlternativesUseStableIdentityAndRemainInspectable()
    {
        var fixture = new CatalogueFixture();
        var output = fixture.Stack(10, "game:board");
        var catalogue = fixture.Build(fixture.Recipe("mod:z", output), fixture.Recipe("mod:a", output));
        var view = new CraftingCatalogueView(catalogue);

        Assert.Equal("a", view.GetSelection(0).Recipe.Recipe.Name!.Path);
        Assert.Equal(2, Assert.Single(Query(view, "board")).Output.Alternatives.Length);
        Assert.False(view.GetSelection(0).CanPlace);
        Assert.Null(view.GetSelection(0).Evaluation.MissingUnits);
    }

    [Fact]
    public void SupportedRecipesSortBeforeUnsupportedIncludingPinnedUnsupported()
    {
        var fixture = new CatalogueFixture();
        var catalogue = fixture.Build(fixture.Recipe("a"), fixture.Recipe("b"), fixture.Recipe("c"));
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [catalogue.Recipes[1].Id] = Partial(1, 4),
            [catalogue.Recipes[2].Id] = CatalogueFixture.Full()
        });
        var selections = Query(view);
        Assert.Equal(new[] { "c", "b", "a" }, selections.Select(selection => selection.Recipe.Recipe.Name!.Path));
    }

    [Fact]
    public void PartialSupportedRecipeAllowsPlacementAndSharesItsMissingResult()
    {
        var fixture = new CatalogueFixture();
        var catalogue = fixture.Build(fixture.Recipe("partial"));
        var view = new CraftingCatalogueView(catalogue);
        var partial = Partial(1, 4);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> { [catalogue.Recipes[0].Id] = partial });

        var selection = view.GetSelection(0);
        Assert.True(selection.CanPlace);
        Assert.False(selection.Evaluation.CanCraft);
        Assert.Same(partial, selection.Evaluation);
        Assert.Equal(0.25, selection.Evaluation.Relevance);
        Assert.Equal(3, Assert.Single(selection.Evaluation.Missing).Quantity);
    }

    [Fact]
    public void ReplacingEvaluationsDropsOldScoresWithoutRetainingCallerDictionary()
    {
        var fixture = new CatalogueFixture();
        var catalogue = fixture.Build(fixture.Recipe("one"));
        var view = new CraftingCatalogueView(catalogue);
        var scores = new Dictionary<int, CraftingRecipeEvaluation> { [catalogue.Recipes[0].Id] = CatalogueFixture.Full() };
        view.ReplaceEvaluations(scores);
        scores.Clear();
        Assert.True(view.GetSelection(0).CanPlace);
        view.ReplaceEvaluations(scores);
        Assert.False(view.GetSelection(0).CanPlace);
        Assert.Null(view.GetSelection(0).Evaluation.Relevance);
    }

    [Fact]
    public void InvalidSelectionsAndResultsSurfaceErrorsWithoutChangingPreviousState()
    {
        var fixture = new CatalogueFixture();
        var catalogue = fixture.Build(fixture.Recipe("one"), fixture.Recipe("two"));
        var view = new CraftingCatalogueView(catalogue);
        view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> { [catalogue.Recipes[0].Id] = CatalogueFixture.Full() });
        Assert.Throws<ArgumentOutOfRangeException>(() => view.GetSelection(-1));
        Assert.Throws<ArgumentException>(() => view.SelectAlternative(0, catalogue.Recipes[1].Id));
        Assert.Throws<ArgumentException>(() => view.ReplaceEvaluations(new Dictionary<int, CraftingRecipeEvaluation> {
            [999] = CatalogueFixture.Full()
        }));
        Assert.True(view.GetSelection(0).CanPlace);
    }

    [Theory]
    [InlineData(-1, 2, false)]
    [InlineData(3, 2, false)]
    [InlineData(1, 2, true)]
    public void EvaluationRejectsContradictoryScores(int allocated, int required, bool craftable)
    {
        Assert.ThrowsAny<ArgumentException>(() => CraftingRecipeEvaluation.Supported(
            allocated, required, craftable, ImmutableArray<CraftingMissingRequirement>.Empty));
    }

    private static CraftingRecipeEvaluation Partial(int allocated, int required) =>
        CraftingRecipeEvaluation.Supported(allocated, required, false,
            ImmutableArray.Create(new CraftingMissingRequirement("A", required - allocated)));

    private static ImmutableArray<CraftingRecipeSelection> Query(CraftingCatalogueView view,
        string? text = null, string? category = null)
    {
        using var filters = new RecipeFilterSet();
        var search = new SearchFilter(text);
        if (!search.IsEmpty) filters.Register("test:search", search);
        if (category != null) filters.Register("test:category", new CategoryFilter(category));
        return view.GetRows(filters);
    }

    private sealed class ObservingFilter(List<CraftingRecipeSelection> observed) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection)
        {
            observed.Add(selection);
            return true;
        }
    }
}
