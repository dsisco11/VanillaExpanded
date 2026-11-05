using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingCatalogueViewTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingCatalogueViewTests
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
        using var filters = new RecipeFilterSet();
        Assert.Equal(new[] { "low", "missing-two", "name-a", "not-craftable-full", "unsupported", "stable-a", "stable-z", "name-z", "full" },
            view.GetRows(filters, new NameOrder()).Select(row => row.Recipe.Recipe.Name!.Path));
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
}
