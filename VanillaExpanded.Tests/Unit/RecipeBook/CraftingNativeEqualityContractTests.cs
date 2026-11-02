using System.Diagnostics;
using Moq;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookPerformanceTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingNativeEqualityContractTests
{
    [Fact]
    public void LookupAndPreviewSharingUseNativeEqualityWithoutSerializingAttributes()
    {
        var f = new CatalogueFixture();
        var attribute = new Mock<IAttribute>();
        attribute.Setup(value => value.GetAttributeId()).Returns(127);
        attribute.Setup(value => value.GetValue()).Returns("opaque");
        attribute.Setup(value => value.Clone()).Returns(() => attribute.Object);
        attribute.Setup(value => value.Equals(It.IsAny<IWorldAccessor>(), It.IsAny<IAttribute>()))
            .Returns((IWorldAccessor _, IAttribute other) => Equals(other.GetValue(), "opaque"));
        attribute.Setup(value => value.ToBytes(It.IsAny<BinaryWriter>())).Throws<InvalidOperationException>();
        attribute.Setup(value => value.ToJsonToken()).Throws<InvalidOperationException>();
        var stack = f.Stack(1, "game:stick");
        stack.Attributes["opaque"] = attribute.Object;
        stack.TempAttributes["opaque"] = attribute.Object;
        var first = f.Recipe("first", stack, 4);
        var second = f.Recipe("second", CraftingStackSnapshot.Copy(stack), 8);
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Index.Add(predicate.Object, new List<IRecipeBase> { first, second });

        var catalogue = f.Build(first, second);
        Assert.Equal(catalogue.Recipes[0].PreviewId, catalogue.Recipes[1].PreviewId);
        var lookup = catalogue.CreateLookup();
        Assert.Equal(new[] { first, second }, lookup.Match(stack));
        Assert.Equal(new[] { first, second }, lookup.Match(CraftingStackSnapshot.Copy(stack)));
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Once);
        attribute.Verify(value => value.ToBytes(It.IsAny<BinaryWriter>()), Times.Never);
        attribute.Verify(value => value.ToJsonToken(), Times.Never);
    }

    [Fact]
    public void AttributeInsertionOrderDoesNotDuplicateNativeChecksOrPreviewIcons()
    {
        var f = new CatalogueFixture();
        var firstStack = f.Stack(1, "game:stick");
        firstStack.Attributes.SetInt("a", 1);
        firstStack.Attributes.SetInt("b", 2);
        firstStack.TempAttributes.SetString("a", "first");
        firstStack.TempAttributes.SetString("b", "second");
        var secondStack = f.Stack(1, "game:stick");
        secondStack.Attributes.SetInt("b", 2);
        secondStack.Attributes.SetInt("a", 1);
        secondStack.TempAttributes.SetString("b", "second");
        secondStack.TempAttributes.SetString("a", "first");
        var first = f.Recipe("first", firstStack);
        var second = f.Recipe("second", secondStack);
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Index.Add(predicate.Object, new List<IRecipeBase> { first, second });
        var catalogue = f.Build(first, second);
        Assert.Equal(catalogue.Recipes[0].PreviewId, catalogue.Recipes[1].PreviewId);
        var lookup = catalogue.CreateLookup();
        Assert.Equal(new[] { first, second }, lookup.Match(firstStack));
        Assert.Equal(new[] { first, second }, lookup.Match(secondStack));
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedAttributeEqualityUsesNativeNumericSemantics(bool temporary)
    {
        var f = new CatalogueFixture();
        var integer = f.Stack(1, "game:stick");
        var integerTree = new TreeAttribute();
        integerTree.SetInt("typed", 1);
        (temporary ? integer.TempAttributes : integer.Attributes)["nested"] = integerTree;
        var longInteger = f.Stack(1, "game:stick");
        var longTree = new TreeAttribute();
        longTree.SetLong("typed", 1);
        (temporary ? longInteger.TempAttributes : longInteger.Attributes)["nested"] = longTree;
        Assert.True(integerTree.Equals(f.World.Object, longTree));
        var first = f.Recipe("integer", integer);
        var second = f.Recipe("long", longInteger);
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Index.Add(predicate.Object, new List<IRecipeBase> { second });
        var catalogue = f.Build(first, second);
        Assert.Equal(catalogue.Recipes[0].PreviewId, catalogue.Recipes[1].PreviewId);
        var lookup = catalogue.CreateLookup();
        Assert.Equal(new[] { second }, lookup.Match(integer));
        Assert.Equal(new[] { second }, lookup.Match(longInteger));
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Once);
    }

    [Fact]
    public void LookupAndPreviewSharingHonorTheCollectiblesNativeEqualityOverride()
    {
        var f = new CatalogueFixture();
        var item = new NativeEqualityItem(99) { Code = new AssetLocation("game:output") };
        var firstStack = new ItemStack(item);
        firstStack.Attributes.SetString("variant", "red");
        var secondStack = firstStack.Clone();
        secondStack.Attributes.SetString("variant", "blue");
        var first = f.Recipe("first", firstStack);
        var second = f.Recipe("second", secondStack);
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Index.Add(predicate.Object, new List<IRecipeBase> { first, second });
        var catalogue = f.Build(first, second);
        Assert.Equal(catalogue.Recipes[0].PreviewId, catalogue.Recipes[1].PreviewId);
        var lookup = catalogue.CreateLookup();
        Assert.Equal(new[] { first, second }, lookup.Match(firstStack));
        Assert.Equal(new[] { first, second }, lookup.Match(secondStack));
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.Once);
        Assert.True(item.Comparisons >= 2);
    }
}
