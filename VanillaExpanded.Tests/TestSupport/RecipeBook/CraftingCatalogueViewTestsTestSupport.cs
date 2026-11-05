using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class CraftingCatalogueViewTestsTestSupport
{

internal static CraftingRecipeEvaluation Partial(int allocated, int required) =>
        CraftingRecipeEvaluation.Supported(allocated, required, false,
            ImmutableArray.Create(new CraftingMissingRequirement("A", required - allocated)));

    internal static ImmutableArray<CraftingRecipeSelection> Query(CraftingCatalogueView view,
        string? text = null, string? category = null)
    {
        using var filters = new RecipeFilterSet();
        var search = new SearchFilter(text);
        if (!search.IsEmpty) filters.Register("test:search", search);
        if (category != null) filters.Register("test:category", new CategoryFilter(category));
        return view.GetRows(filters);
    }

    internal sealed class ObservingFilter(List<CraftingRecipeSelection> observed) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection)
        {
            observed.Add(selection);
            return true;
        }
    }

}
