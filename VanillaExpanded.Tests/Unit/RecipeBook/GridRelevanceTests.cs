using System.Collections.Immutable;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
public sealed class GridRelevanceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void RepeatedStacksAndDifferentAmountsCountAsOneKindRegardlessOfGridPosition(int quantity)
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", quantity);
        f.Grid[0].Itemstack = board;
        f.Grid[8].Itemstack = CraftingStackSnapshot.Copy(board);
        var recipe = f.Recipe(f.Ingredient(board, 4), f.Ingredient(board, 4));
        var allocation = f.Allocate(recipe);
        Assert.True(allocation.Evaluation.IsSupported);
        Assert.Equal(1, allocation.GridMatches);
        Assert.Equal(quantity, board.StackSize);
        f.Grid[0].Itemstack = null;
        f.Grid[8].Itemstack = null;
        Assert.Equal(0, f.Allocate(recipe, board).GridMatches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingReassignsWildcardsButOnePositionCannotUseTwoKinds(bool shapeless)
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 1);
        var pine = f.Stack(2, "board-pine", 1);
        f.Grid[0].Itemstack = oak;
        f.Grid[8].Itemstack = pine;
        var recipe = f.Recipe(f.Wildcard("board-*", 4), f.Ingredient(oak, 4));
        recipe.Shapeless = shapeless;
        var allocation = f.Allocate(recipe);
        Assert.True(allocation.Evaluation.IsSupported);
        Assert.False(allocation.Evaluation.CanCraft);
        Assert.Equal(2, allocation.GridMatches);
        Assert.Equal(1, f.Allocate(f.Recipe(f.Wildcard("board-*", 4))).GridMatches);
    }

    [Fact]
    public void PersistentAttributeVariantsRemainDistinctAndRespectExactRequirements()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board", 1);
        oak.Attributes.SetString("wood", "oak");
        var pine = CraftingStackSnapshot.Copy(oak);
        pine.Attributes.SetString("wood", "pine");
        f.Grid[0].Itemstack = oak;
        f.Grid[1].Itemstack = pine;
        var recipe = f.Recipe(f.Ingredient(oak, 4), f.Ingredient(pine, 4));
        Assert.Equal(2, f.Allocate(recipe).GridMatches);
        Assert.Equal(1, f.Allocate(f.Recipe(f.Ingredient(oak, 4), f.Ingredient(oak, 4))).GridMatches);
    }

    [Theory]
    [InlineData("oak", 1)]
    [InlineData("pine", 0)]
    [InlineData("birch", 0)]
    public void NativeWildcardMatchingHonorsAllowedAndSkippedVariants(string variant, int expected)
    {
        var f = new AllocationFixture();
        f.Grid[0].Itemstack = f.Stack(1, "board-" + variant, 1);
        var ingredient = f.Wildcard("board-*", 4);
        ingredient.AllowedVariants = ["oak", "pine"];
        ingredient.SkipVariants = ["pine"];
        Assert.Equal(expected, f.Allocate(f.Recipe(ingredient)).GridMatches);
    }

    [Fact]
    public void NativeTagMatchingHonorsRequiredAndForbiddenTags()
    {
        var f = new AllocationFixture();
        var allowed = f.Stack(1, "allowed", 1);
        allowed.Collectible.Tags = Tags(1);
        var forbidden = f.Stack(2, "forbidden", 1);
        forbidden.Collectible.Tags = Tags(1, 2);
        var ingredient = f.Wildcard("*", 4);
        ingredient.Tags = new ComplexTagCondition<TagSet>
        {
            conditions = [new() { RequiredTags = Tags(1), ForbiddenTags = Tags(2) }]
        };
        var recipe = f.Recipe(ingredient, ingredient.Clone());
        f.Grid[0].Itemstack = allowed;
        f.Grid[1].Itemstack = forbidden;
        Assert.Equal(1, f.Allocate(recipe).GridMatches);
        f.Grid[0].Itemstack = null;
        Assert.Equal(0, f.Allocate(recipe).GridMatches);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(1, 0)]
    public void ToolMatchesRespectNativeDurabilityPredicates(int durability, int expected)
    {
        var f = new AllocationFixture();
        var tool = f.Stack(1, "saw", 1, maxStack: 1);
        tool.Collectible.Durability = 10;
        tool.Attributes.SetInt("durability", durability);
        var ingredient = f.Ingredient(tool);
        ingredient.ResolvedItemStack!.Attributes.RemoveAttribute("durability");
        ingredient.Consume = false;
        ingredient.DurabilityChange = -2;
        f.Grid[0].Itemstack = tool;
        Assert.Equal(expected, f.Allocate(f.Recipe(ingredient)).GridMatches);
    }

    [Fact]
    public void SameKindRetainsTemporaryVariantsAndPredicateMutationsCannotEscape()
    {
        var f = new AllocationFixture();
        var item = new Mock<MockItem>(1, (byte)0, f.Api.Object) { CallBase = true };
        item.Object.Code = new AssetLocation("game:board");
        item.Setup(value => value.MatchesForCrafting(It.IsAny<ItemStack>(), It.IsAny<IRecipeBase>(),
                It.IsAny<IRecipeIngredient>()))
            .Returns((ItemStack stack, IRecipeBase _, IRecipeIngredient _) =>
            {
                bool ready = stack.TempAttributes.GetBool("ready");
                stack.Attributes.SetBool("mutated", true);
                stack.TempAttributes.SetBool("mutated", true);
                Assert.Equal(4, stack.StackSize);
                return ready;
            });
        var cold = new ItemStack(item.Object);
        var ready = CraftingStackSnapshot.Copy(cold);
        ready.TempAttributes.SetBool("ready", true);
        f.Grid[0].Itemstack = cold;
        f.Grid[1].Itemstack = ready;
        var recipe = f.Recipe(f.Ingredient(cold, 4), f.Ingredient(cold, 4));
        var snapshot = f.Snapshot();
        var grid = new GridRelevance(f.Catalogue.World.Object, snapshot.GridStacks);
        Assert.Equal(1, grid.Count(recipe, Requirements(recipe)));
        Assert.All(new[] { cold, ready }.Concat(snapshot.GridStacks.Select(stack => stack.GetStack())), stack =>
        {
            Assert.False(stack.Attributes.HasAttribute("mutated"));
            Assert.False(stack.TempAttributes.HasAttribute("mutated"));
            Assert.Equal(1, stack.StackSize);
        });
    }

    [Fact]
    public void NativePredicateFailuresPropagateWithoutChangingTheSnapshot()
    {
        var f = new AllocationFixture();
        var item = new Mock<MockItem>(1, (byte)0, f.Api.Object) { CallBase = true };
        item.Object.Code = new AssetLocation("game:board");
        item.Setup(value => value.MatchesForCrafting(It.IsAny<ItemStack>(), It.IsAny<IRecipeBase>(),
            It.IsAny<IRecipeIngredient>())).Throws<InvalidOperationException>();
        var stack = new ItemStack(item.Object);
        f.Grid[0].Itemstack = stack;
        var recipe = f.Recipe(f.Ingredient(stack, 4));
        var snapshot = f.Snapshot();
        var grid = new GridRelevance(f.Catalogue.World.Object, snapshot.GridStacks);
        Assert.Throws<InvalidOperationException>(() => grid.Count(recipe, Requirements(recipe)));
        Assert.Equal(1, Assert.Single(snapshot.GridStacks).Quantity);
        recipe.CopyAttributesFrom = "I";
        var unsupported = f.Allocate(recipe);
        Assert.False(unsupported.Evaluation.IsSupported);
        Assert.Equal(0, unsupported.GridMatches);
    }

    [Fact]
    public void MaximumMatchingAgreesWithAllThreeByThreeGraphPermutations()
    {
        var f = new AllocationFixture();
        int mask = 0;
        for (int kind = 0; kind < 3; kind++)
        {
            int row = kind;
            var item = new Mock<MockItem>(kind + 1, (byte)0, f.Api.Object) { CallBase = true };
            item.Object.Code = new AssetLocation("game:board-" + kind);
            item.Setup(value => value.MatchesForCrafting(It.IsAny<ItemStack>(), It.IsAny<IRecipeBase>(),
                    It.IsAny<IRecipeIngredient>()))
                .Returns((ItemStack _, IRecipeBase _, IRecipeIngredient ingredient) =>
                    (mask & 1 << (row * 3 + ingredient.Quantity - 1)) != 0);
            f.Grid[kind].Itemstack = new ItemStack(item.Object);
        }
        var recipe = f.Recipe(Enumerable.Range(1, 3).Select(quantity => f.Wildcard("board-*", quantity)).ToArray());
        var grid = new GridRelevance(f.Catalogue.World.Object, f.Snapshot().GridStacks);
        int[][] permutations = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        for (mask = 0; mask < 512; mask++)
        {
            int expected = permutations.Max(permutation => Enumerable.Range(0, 3)
                .Count(kind => (mask & 1 << (kind * 3 + permutation[kind])) != 0));
            Assert.Equal(expected, grid.Count(recipe, Requirements(recipe)));
        }
    }

    [Fact]
    public void NineDistinctInputsCanCoverNineWildcardPositions()
    {
        var f = new AllocationFixture();
        for (int i = 0; i < 9; i++) f.Grid[i].Itemstack = f.Stack(i + 1, "board-" + i, 1);
        var recipe = f.Recipe(Enumerable.Range(0, 9).Select(_ => f.Wildcard("board-*", 4)).ToArray());
        var grid = new GridRelevance(f.Catalogue.World.Object, f.Snapshot().GridStacks);
        Assert.Equal(9, grid.Count(recipe, Requirements(recipe)));
    }

    private static ImmutableArray<CraftingRequirement> Requirements(GridRecipe recipe) =>
        recipe.ResolvedIngredients!.Select((ingredient, index) => new CraftingRequirement(index, "need-" + index, ingredient!))
            .ToImmutableArray();

    private static TagSet Tags(params ushort[] handles)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write((ushort)handles.Length);
        foreach (ushort handle in handles) writer.Write(handle);
        writer.Flush();
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);
        return TagSet.FromBytes(reader);
    }
}
