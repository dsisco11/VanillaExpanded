using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeFilterSetTestsTestSupport
{

internal static ImmutableArray<CraftingRecipeSelection> Rows()
    {
        var fixture = new CatalogueFixture();
        var view = new CraftingCatalogueView(fixture.Build(
            fixture.Recipe("a"), fixture.Recipe("b"), fixture.Recipe("c")));
        using var filters = new RecipeFilterSet();
        return view.GetRows(filters);
    }

    internal sealed class Provider(Func<CraftingRecipeSelection, bool> matches) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => matches(selection);
    }

}
