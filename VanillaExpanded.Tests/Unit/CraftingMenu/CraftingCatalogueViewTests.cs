using System.Collections.Immutable;
using VanillaExpanded.CraftingMenu;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public class CraftingCatalogueViewTests
{
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

        Assert.Single(view.Search(query));
        Assert.Empty(view.Search("érable iron"));
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

        Assert.Equal(new[] { "high", "low" }, view.Search("output").Select(selection => selection.Recipe.Recipe.Name!.Path));
        Assert.Empty(view.Search("no-such-output"));
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
        Assert.Equal(2, Assert.Single(view.Search("board")).Output.Alternatives.Length);
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
        var selections = view.Search("");
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
}
