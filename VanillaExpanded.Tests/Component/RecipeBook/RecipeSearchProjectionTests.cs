using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingCatalogueViewTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
public sealed class RecipeSearchProjectionTests
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
}
