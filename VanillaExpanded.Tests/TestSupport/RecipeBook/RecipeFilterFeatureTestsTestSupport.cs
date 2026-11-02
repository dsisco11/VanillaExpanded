using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeFilterFeatureTestsTestSupport
{

internal sealed class MutationFilter(Action mutate) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection)
        {
            mutate();
            return true;
        }
    }

}
