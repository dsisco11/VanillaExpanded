using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingCatalogueViewTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingRecipeEvaluationTests
{
    [Theory]
    [InlineData(-1, 2, false)]
    [InlineData(3, 2, false)]
    [InlineData(1, 2, true)]
    public void EvaluationRejectsContradictoryScores(int allocated, int required, bool craftable)
    {
        Assert.ThrowsAny<ArgumentException>(() => CraftingRecipeEvaluation.Supported(
            allocated, required, craftable, ImmutableArray<CraftingMissingRequirement>.Empty));
    }
}
