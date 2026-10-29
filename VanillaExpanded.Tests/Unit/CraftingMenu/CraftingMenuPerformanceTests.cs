using System.Diagnostics;
using Moq;
using VanillaExpanded.CraftingMenu;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;

namespace VanillaExpanded.Tests.Unit.CraftingMenu;

[Trait("Category", "Unit")]
public sealed class CraftingMenuPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void CatalogueRefreshProfile()
    {
        var f = new AllocationFixture();
        var recipes = Enumerable.Range(0, 2000).Select(id =>
        {
            var ingredient = f.Ingredient(f.Stack(id < 100 ? 1 : id + 2, id < 100 ? "board" : "other-" + id, 1));
            return f.Recipe(Enumerable.Repeat(ingredient, 9).ToArray());
        }).ToArray();
        f.Register(recipes);
        var catalogue = f.Catalogue.Build(recipes);
        var snapshot = f.Snapshot(f.Stack(1, "board", 64));
        var timer = Stopwatch.StartNew();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var results = f.Allocator.EvaluateCatalogue(catalogue, snapshot);
        timer.Stop();
        output.WriteLine($"2000 recipes, 9 positions, 100 attention candidates: {timer.Elapsed.TotalMilliseconds:F1} ms, "
            + $"{(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024d / 1024:F2} MiB");
        Assert.Equal(2000, results.Count);
    }

    [Fact]
    public void RepeatedWildcardAllocationProfile()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(Enumerable.Range(0, 9).Select(_ => f.Wildcard("board-*")).ToArray());
        var stock = Enumerable.Range(0, 8).Select(id => f.Stack(id + 1, "board-" + id, 64)).ToArray();
        f.Register(recipe);
        var entry = f.Catalogue.Build(recipe).Recipes[0];
        var snapshot = f.Snapshot(stock);
        var timer = Stopwatch.StartNew();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var result = f.Allocator.Evaluate(entry, snapshot);
        output.WriteLine($"9 repeated wildcard positions, 8 incompatible variants: {timer.Elapsed.TotalMilliseconds:F1} ms, "
            + $"{(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024d / 1024:F2} MiB");
        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(32, result.CompleteExecutions);
        Assert.Equal(512, result.Shift.Sum(placement => placement.Quantity));
    }

    [Fact]
    public void NativeIngredientChecksAreSharedAcrossRecipePositionsAndRecipesButNotRefreshes()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 64);
        var recipes = Enumerable.Range(0, 100)
            .Select(_ => f.Recipe(Enumerable.Range(0, 9).Select(_ => f.Ingredient(board)).ToArray())).ToArray();
        var predicate = new Mock<IRecipeIngredientBase>();
        predicate.Setup(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false)).Returns(true);
        f.Catalogue.Index.Add(predicate.Object, recipes.Cast<IRecipeBase>().ToList());
        var catalogue = f.Catalogue.Build(recipes);
        var snapshot = f.Snapshot(board);
        f.Allocator.EvaluateCatalogue(catalogue, snapshot);
        // Stock (64), ordinary placement (1), and bulk placement (7/8).
        predicate.Verify(value => value.SatisfiesAsIngredient(It.IsAny<ItemStack>(), false), Times.AtMost(4));
        int checks = predicate.Invocations.Count;
        Assert.True(checks > 0);
        f.Allocator.EvaluateCatalogue(catalogue, snapshot);
        Assert.Equal(checks * 2, predicate.Invocations.Count);
    }

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

    [Fact]
    public void CollectibleBehaviorsDoNotBlanketRejectAllocation()
    {
        var f = new AllocationFixture();
        var stack = f.Stack(1, "a", 1);
        stack.Collectible.CollectibleBehaviors = new[] { new Mock<CollectibleBehavior>(stack.Collectible).Object };
        var recipe = f.Recipe(f.Ingredient(stack));
        var output = recipe.Output!.ResolvedItemStack!.Collectible;
        output.CollectibleBehaviors = new[] { new Mock<CollectibleBehavior>(output).Object };
        var result = f.Allocate(recipe, stack);
        Assert.True(result.Evaluation.IsSupported);
        Assert.True(result.Evaluation.CanCraft);
        Assert.Single(result.Ordinary);
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

    [Fact]
    public void ActionEvaluationRejectsWrongThreadBeforeReadingNativeIndex()
    {
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        var entry = f.Catalogue.Build(recipe).Recipes[0];
        var snapshot = f.Snapshot();
        f.Catalogue.World.Invocations.Clear();
        Exception? error = null;
        var worker = new Thread(() => error = Record.Exception(() => f.Allocator.Evaluate(entry, snapshot)));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(error);
        Assert.Empty(f.Catalogue.World.Invocations);
    }

    private sealed class NativeEqualityItem(int id) : VanillaExpanded.Tests.Mocks.MockItem(id)
    {
        internal int Comparisons { get; private set; }

        public override bool Equals(ItemStack thisStack, ItemStack otherStack, params string[] ignoreAttributeSubTrees)
        {
            Comparisons++;
            return thisStack.Class == otherStack.Class && thisStack.Id == otherStack.Id;
        }
    }

    private sealed class BroadItem : Item
    {
        public override bool Satisfies(ItemStack thisStack, ItemStack otherStack) => true;
    }
}
