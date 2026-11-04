using System.Diagnostics;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookPerformanceTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingIngredientLookupTests
{
    [Fact]
    public void EvaluationLookupKeepsNativeOrderingAndPersistentAttributeChanges()
    {
        var f = new CatalogueFixture();
        var first = f.Recipe("first");
        var second = f.Recipe("second");
        var one = new Mock<IRecipeIngredientBase>();
        var many = new Mock<IRecipeIngredientBase>();
        one.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false))
            .Returns((ItemStack stack, bool _) => stack.Attributes.GetString("state") == "ready");
        many.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false))
            .Returns((ItemStack stack, bool _) => stack.Attributes.GetString("other") == "ready");
        f.Index.Add(many.Object, new List<IRecipeBase> { second, first });
        f.Index.Add(one.Object, new List<IRecipeBase> { first });
        var catalogue = f.Build(first, second);
        var lookup = catalogue.CreateLookup();
        var stack = f.Stack(1, "game:stick");
        Assert.Empty(lookup.Match(stack));
        stack.Attributes.SetString("state", "ready");
        Assert.Equal(new[] { first }, lookup.Match(stack));
        stack.StackSize = 2;
        Assert.Equal(new[] { first }, lookup.Match(stack));
        one.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Exactly(2));
        stack.Attributes.SetString("other", "ready");
        Assert.Equal(new[] { second, first }, lookup.Match(stack));
        stack.StackSize = 1;
        stack.Attributes.RemoveAttribute("other");
        Assert.Equal(new[] { first }, lookup.Match(stack));
        one.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Exactly(3));
    }

    [Fact]
    public void LookupPreservesNativeOrderAndPredicatesAcrossExactAndCustomIngredients()
    {
        var f = new CatalogueFixture();
        var recipes = Enumerable.Range(1, 2000).Select(id =>
        {
            var recipe = f.Recipe("recipe-" + id);
            var stack = f.Stack(id, "game:input-" + id);
            recipe.ResolvedIngredients![0]!.ResolvedItemStack = stack;
            recipe.ResolvedIngredients[0]!.Code = stack.Collectible.Code;
            f.Register(recipe);
            return recipe;
        }).ToArray();
        var broad = new Mock<IRecipeIngredientBase>();
        broad.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Index.Add(broad.Object, new List<IRecipeBase> { recipes[0] });
        var index = f.World.Object.FastSearchRecipesByIngredient.ToArray();
        var input = f.Stack(1000, "game:input-1000");
        var lookup = new CraftingIngredientLookup(f.World.Object, index);
        Assert.Equal(new[] { recipes[999], recipes[0] }, lookup.Match(input));
        input.Attributes.SetString("extra", "allowed");
        Assert.Equal(new[] { recipes[999], recipes[0] }, lookup.Match(input));
    }

    [Fact]
    public void CustomExactCollectibleSatisfactionIsNotIncorrectlyBucketedByItemId()
    {
        var f = new CatalogueFixture();
        var recipe = f.Recipe("custom");
        recipe.ResolvedIngredients![0]!.ResolvedItemStack = new ItemStack(new BroadItem { ItemId = 900,
            Code = new AssetLocation("game:any") });
        f.Register(recipe);
        var index = f.World.Object.FastSearchRecipesByIngredient.ToArray();
        var input = f.Stack(1, "game:stick");
        Assert.Equal(new[] { recipe }, new CraftingIngredientLookup(f.World.Object, index).Match(input));
    }
}
