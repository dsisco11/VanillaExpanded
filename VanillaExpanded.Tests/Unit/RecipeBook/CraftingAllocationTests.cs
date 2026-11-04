using VanillaExpanded.RecipeBook;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using System.Diagnostics;
using Moq;
using Vintagestory.API.Datastructures;
using Xunit.Abstractions;
using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookPerformanceTestsTestSupport;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.CraftingAllocationTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public sealed class CraftingAllocationTests
{
    [Fact]
    public void ExistingGridSupply_IsCountedOnce()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        f.Grid[0].Itemstack = board;
        var recipe = f.Recipe(f.Ingredient(board), f.Ingredient(board));
        var result = f.Allocate(recipe);
        Assert.Equal(1, result.Evaluation.Allocated);
        Assert.False(result.Evaluation.CanCraft);
        Assert.Single(result.Ordinary);
        Assert.Equal(f.Grid.InventoryID, Assert.Single(result.Ordinary[0].Sources).Slot.InventoryId);
    }

    [Fact]
    public void GridSlotLimit_CapsPlacementButNotOtherPositions()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 20);
        f.Grid[0].MaxSlotStackSize = 3;
        var result = f.Allocate(f.Recipe(f.Ingredient(board), f.Ingredient(board)), board);
        Assert.Equal(new[] { 3, 17 }, result.Shift.Select(placement => placement.Quantity));
        Assert.Equal(3, result.CompleteExecutions);
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

    [Fact]
    public void CountedRequirements_ShareCoverageAndMissingData()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 3);
        var stick = f.Stack(2, "stick", 1);
        var recipe = f.Recipe(f.Ingredient(board, 4), f.Ingredient(stick, 2));
        var result = f.Allocate(recipe, board, stick);

        Assert.True(result.Evaluation.IsSupported);
        Assert.Equal(4, result.Evaluation.Allocated);
        Assert.Equal(6, result.Evaluation.Required);
        Assert.Equal(4d / 6, result.Evaluation.Relevance);
        Assert.False(result.Evaluation.CanCraft);
        Assert.Equal(new[] { 1, 1 }, result.Evaluation.Missing.Select(missing => missing.Quantity));
        Assert.Equal(new[] { 3, 1 }, result.Ordinary.Select(placement => placement.Quantity));
        Assert.Equal(result.Requirements.Select(need => need.Id), result.Evaluation.Missing.Select(missing => missing.IngredientId));

        board.StackSize = 64;
        result = f.Allocate(recipe, board);
        Assert.Equal(4, result.Evaluation.Allocated);
        Assert.Equal(2, Assert.Single(result.Evaluation.Missing).Quantity);
        Assert.Equal(64, Assert.Single(result.Shift).Quantity);
        Assert.Equal(0, result.CompleteExecutions);
    }

    [Theory]
    [InlineData(20, 10, 10)]
    [InlineData(127, 64, 63)]
    [InlineData(1, 1, 0)]
    public void RepeatedPositions_SpreadCoverageThenBatchThenSurplus(int stock, int first, int second)
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", stock);
        var recipe = f.Recipe(f.Ingredient(board), f.Ingredient(board));
        var result = f.Allocate(recipe, board);

        Assert.True(result.Evaluation.IsSupported);
        Assert.Equal(new[] { first, second }.Where(value => value > 0),
            result.Shift.Select(placement => placement.Quantity));
        Assert.Equal(Math.Min(stock / 2, 64), result.CompleteExecutions);
        Assert.Equal(Math.Min(stock, 2), result.Ordinary.Sum(placement => placement.Quantity));
        f.AssertConserved(result);
    }

    [Fact]
    public void Overlap_ReservesOnlyOakForRestrictedPosition()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 1);
        var pine = f.Stack(2, "board-pine", 1);
        var recipe = f.Recipe(f.Wildcard("board-*"), f.Ingredient(oak));
        var result = f.Allocate(recipe, oak, pine);

        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(new[] { pine.Id, oak.Id }, result.Ordinary.Select(placement => placement.GetStack().Id));
        f.AssertConserved(result);
    }

    [Fact]
    public void Shift_ReconsidersEarlyOakForLargerPineBatch()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 1);
        var pine = f.Stack(2, "board-pine", 64);
        var recipe = f.Recipe(f.Wildcard("board-*"));
        var result = f.Allocate(recipe, oak, pine);

        Assert.Equal(oak.Id, Assert.Single(result.Ordinary).GetStack().Id);
        Assert.Equal(pine.Id, Assert.Single(result.Shift).GetStack().Id);
        Assert.Equal(64, Assert.Single(result.Shift).Quantity);
        Assert.Equal(64, result.CompleteExecutions);
    }

    [Fact]
    public void OnePosition_CannotMergeIncompatibleMatchingVariants()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 2);
        var pine = f.Stack(2, "board-pine", 2);
        var recipe = f.Recipe(f.Wildcard("board-*", 4));
        var result = f.Allocate(recipe, oak, pine);

        Assert.Equal(2, result.Evaluation.Allocated);
        Assert.Equal(2, Assert.Single(result.Evaluation.Missing).Quantity);
        Assert.False(result.Evaluation.CanCraft);
        Assert.Equal(2, Assert.Single(result.Shift).Quantity);
    }

    [Fact]
    public void Wildcards_HonorAllowedAndSkippedVariants()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 1);
        var pine = f.Stack(2, "board-pine", 64);
        var birch = f.Stack(3, "board-birch", 64);
        var ingredient = f.Wildcard("board-*");
        ingredient.AllowedVariants = new[] { "oak", "pine" };
        ingredient.SkipVariants = new[] { "pine" };
        var result = f.Allocate(f.Recipe(ingredient), oak, pine, birch);

        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(oak.Id, Assert.Single(result.Shift).GetStack().Id);
        Assert.Equal(1, result.CompleteExecutions);
    }

    [Fact]
    public void ExactAttributes_AreNotReducedToItemCodes()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board", 1); oak.Attributes.SetString("wood", "oak");
        var pine = f.Stack(1, "board", 64); pine.Attributes.SetString("wood", "pine");
        var result = f.Allocate(f.Recipe(f.Ingredient(oak, 2)), pine);

        Assert.Equal(0, result.Evaluation.Allocated);
        Assert.Empty(result.Ordinary);
        Assert.Equal(2, Assert.Single(result.Evaluation.Missing).Quantity);
    }

    [Fact]
    public void Tool_IsPresenceOnlyAndCapsExecutionsWithoutReplacement()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 64);
        var tool = f.Stack(2, "saw", 1, maxStack: 1);
        tool.Collectible.Durability = 10;
        tool.Attributes.SetInt("durability", 5);
        var otherTool = CraftingStackSnapshot.Copy(tool);
        var ingredient = f.Ingredient(tool);
        ingredient.ResolvedItemStack!.Attributes.RemoveAttribute("durability");
        ingredient.IsTool = true;
        ingredient.ToolDurabilityCost = 2;
        var result = f.Allocate(f.Recipe(f.Ingredient(board), ingredient), board, tool, otherTool);

        Assert.True(result.Evaluation.CanCraft);
        Assert.False(result.Requirements[1].IsConsumed);
        Assert.Equal(2, result.Evaluation.Required);
        Assert.Equal(2, result.CompleteExecutions);
        Assert.Equal(new[] { 64, 1 }, result.Shift.Select(placement => placement.Quantity));
        f.AssertConserved(result);
        tool.Attributes.SetInt("durability", 1);
        result = f.Allocate(f.Recipe(f.Ingredient(board), ingredient), board, tool);
        Assert.False(result.Evaluation.CanCraft);
        Assert.Equal(1, Assert.Single(result.Evaluation.Missing).Quantity);
        Assert.Contains("insufficient-durability", result.Evaluation.Missing[0].Reason);
        Assert.Equal(64, Assert.Single(result.Shift).Quantity);
    }

    [Fact]
    public void CurrentNonConsumedToolDefinition_UsesDurabilityChange()
    {
        var f = new AllocationFixture();
        var tool = f.Stack(2, "saw", 1, maxStack: 1);
        tool.Collectible.Durability = 20;
        tool.Attributes.SetInt("durability", 7);
        var ingredient = f.Ingredient(tool);
        ingredient.ResolvedItemStack!.Attributes.RemoveAttribute("durability");
        ingredient.Consume = false;
        ingredient.DurabilityChange = -3;
        var result = f.Allocate(f.Recipe(ingredient), tool);
        Assert.Equal(2, result.CompleteExecutions);
        Assert.Single(result.Shift);
        Assert.Equal(1, result.Evaluation.Required);
    }

    [Fact]
    public void PresenceOnlyRequirement_IsNotDepletedOrReplaced()
    {
        var f = new AllocationFixture();
        var template = f.Stack(1, "template", 1, maxStack: 1);
        var ingredient = f.Ingredient(template);
        ingredient.Consume = false;
        var result = f.Allocate(f.Recipe(ingredient), template);
        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(1, result.Evaluation.Required);
        Assert.Null(result.CompleteExecutions);
        Assert.Equal(1, Assert.Single(result.Shift).Quantity);
        f.AssertConserved(result);
    }

    [Fact]
    public void MaximumFiniteDurability_IsNotReportedAsUnbounded()
    {
        var f = new AllocationFixture();
        var tool = f.Stack(1, "tool", 1, maxStack: 1);
        tool.Collectible.Durability = int.MaxValue;
        var ingredient = f.Ingredient(tool);
        ingredient.Consume = false;
        ingredient.DurabilityChange = -1;
        var result = f.Allocate(f.Recipe(ingredient), tool);
        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(int.MaxValue, result.CompleteExecutions);
    }

    [Fact]
    public void OverflowingNativeDurabilityCost_IsExplicitlyUnsupported()
    {
        var f = new AllocationFixture();
        var tool = f.Stack(1, "tool", 1, maxStack: 1);
        var ingredient = f.Ingredient(tool);
        ingredient.Consume = false;
        ingredient.DurabilityChange = int.MinValue;
        var result = f.Allocate(f.Recipe(ingredient), tool);
        Assert.False(result.Evaluation.IsSupported);
        Assert.Null(result.Evaluation.Relevance);
        Assert.Empty(result.Shift);
    }

    [Fact]
    public void CustomDurabilityMutation_DoesNotRejectPlacementOrInvokeConsumptionCallbacks()
    {
        var f = new AllocationFixture();
        var plain = f.Stack(1, "tool", 1, maxStack: 1);
        var custom = new ItemStack(new CustomDamageItem(1) {
            Code = plain.Collectible.Code, MaxStackSize = 1, Durability = 10
        });
        var ingredient = f.Ingredient(plain);
        ingredient.Consume = false;
        ingredient.DurabilityChange = -1;
        var result = f.Allocate(f.Recipe(ingredient), custom);
        Assert.True(result.Evaluation.IsSupported);
        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(10, result.CompleteExecutions);
        Assert.Single(result.Shift);
        Assert.Empty(result.Evaluation.Missing);
    }

    [Fact]
    public void HugeSupply_IsBoundedByUsableGridCapacityWithoutOverflow()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", int.MaxValue);
        var other = CraftingStackSnapshot.Copy(board);
        var result = f.Allocate(f.Recipe(f.Ingredient(board), f.Ingredient(board)), board, other);
        Assert.True(result.Evaluation.CanCraft);
        Assert.Equal(new[] { 64, 64 }, result.Shift.Select(placement => placement.Quantity));
        Assert.Equal(64, result.CompleteExecutions);
        f.AssertConserved(result);
    }

    [Fact]
    public void Shaped_PreservesHolesAndRows_AndShapelessUsesStableDenseLayout()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 2);
        var recipe = f.Recipe(f.Ingredient(board), null, null, f.Ingredient(board));
        recipe.Width = recipe.Height = 2;
        var shaped = f.Allocate(recipe, board);
        Assert.Equal(new[] { 0, 4 }, shaped.Ordinary.Select(placement => placement.GridPosition));
        Assert.True(shaped.Evaluation.CanCraft);
        recipe.Shapeless = true;
        var shapeless = f.Allocate(recipe, board);
        Assert.Equal(new[] { 0, 1 }, shapeless.Ordinary.Select(placement => placement.GridPosition));
        Assert.True(shapeless.Evaluation.CanCraft);
    }

    [Fact]
    public void NativeWinner_UsesIngredientIndexRatherThanRegistryOrder()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var selected = f.Recipe(f.Ingredient(board));
        var other = f.Recipe(f.Ingredient(board));
        f.Register(other, selected);
        f.Catalogue.World.Setup(world => world.GridRecipes).Returns(new List<GridRecipe> { selected, other });
        var result = f.AllocateRegistered(selected, board);

        Assert.False(result.Evaluation.IsSupported);
        Assert.Contains("native-mismatch", result.Evaluation.UnsupportedReason);
        Assert.Null(result.Evaluation.Relevance);
        Assert.Empty(result.Ordinary);
        Assert.Empty(result.Evaluation.Missing);
    }

    [Fact]
    public void NativeEvents_AreActuallyValidatedForCompleteLayouts()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        f.Events.Setup(events => events.TriggerMatchesRecipe(It.IsAny<IPlayer>(), It.IsAny<IRecipeBase>(),
            It.IsAny<ItemSlot[]>())).Returns(false);
        var result = f.Allocate(f.Recipe(f.Ingredient(board)), board);
        Assert.False(result.Evaluation.IsSupported);
        Assert.Contains("native-mismatch", result.Evaluation.UnsupportedReason);
    }

    [Fact]
    public void PotentialCompetition_DoesNotRefusePartialPlacementOrHideDeficits()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var recipe = f.Recipe(f.Ingredient(board, 2));
        var competitor = f.Recipe(f.Ingredient(board));
        f.Register(competitor, recipe);
        var catalogue = f.Catalogue.Build(recipe);
        var result = f.Allocator.Evaluate(catalogue.Recipes[0], f.Snapshot(board));

        Assert.True(result.Evaluation.IsSupported);
        Assert.False(result.Evaluation.CanCraft);
        Assert.Null(result.Evaluation.UnsupportedReason);
        Assert.Equal(0.5, result.Evaluation.Relevance);
        Assert.Equal(1, Assert.Single(result.Evaluation.Missing).Quantity);
        Assert.Equal(1, Assert.Single(result.Ordinary).Quantity);
        Assert.Equal(1, Assert.Single(result.Shift).Quantity);
    }

    [Fact]
    public void ExceptionalIngredientAndDynamicOutput_AreExplicitlyUnsupported()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var custom = new CustomIngredient { Code = board.Collectible.Code, Type = EnumItemClass.Item,
            ResolvedItemStack = board };
        var result = f.Allocate(f.Recipe(custom), board);
        Assert.False(result.Evaluation.IsSupported);
        Assert.Empty(result.Ordinary);
        var dynamic = f.Recipe(f.Ingredient(board));
        dynamic.CopyAttributesFrom = "A";
        Assert.False(f.Allocate(dynamic, board).Evaluation.IsSupported);
    }

    [Fact]
    public void EqualOptima_UseStableInventoryOrderAndCopiesAreDefensive()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 4);
        var pine = f.Stack(2, "board-pine", 4);
        var result = f.Allocate(f.Recipe(f.Wildcard("board-*")), oak, pine);
        var placement = Assert.Single(result.Shift);
        Assert.Equal(oak.Id, placement.GetStack().Id);
        placement.GetStack().StackSize = 999;
        placement.GetStack().TempAttributes.SetInt("changed", 1);
        Assert.Equal(4, placement.Quantity);
        Assert.False(placement.GetStack().TempAttributes.HasAttribute("changed"));
        Assert.Equal(4, oak.StackSize);
        Assert.False(oak.TempAttributes.HasAttribute("changed"));
    }

    [Fact]
    public void NativeAverageDurability_KeepsGenericPreviewWithoutInventingOutputAttributes()
    {
        var f = new AllocationFixture();
        var board = f.Stack(1, "board", 1);
        var recipe = f.Recipe(f.Ingredient(board));
        Assert.True(recipe.AverageDurability);
        var catalogue = f.Catalogue.Build(recipe);
        Assert.True(catalogue.Recipes[0].IsGenericPreview);
        var result = f.Allocate(recipe, board);
        Assert.True(result.Evaluation.CanCraft);
        Assert.False(recipe.Output!.ResolvedItemStack!.Attributes.HasAttribute("durability"));
    }

    [Fact]
    public void EquivalentVariantPositions_BalanceBeforeInventoryTies()
    {
        var f = new AllocationFixture();
        var oak = f.Stack(1, "board-oak", 2);
        var pine = f.Stack(2, "board-pine", 2);
        var recipe = f.Recipe(f.Wildcard("board-*", 2), f.Wildcard("board-*", 2), f.Wildcard("board-*", 2));
        var result = f.Allocate(recipe, oak, pine);
        Assert.Equal(4, result.Evaluation.Allocated);
        Assert.Equal(0, result.CompleteExecutions);
        Assert.Equal(new[] { 2, 1, 1 }, result.Shift.Select(placement => placement.Quantity));
        f.AssertConserved(result);
    }

    [Fact]
    public void SmallBoundedFixtures_MatchExhaustiveFeasibleAllocations()
    {
        // Exhaust every stock pair, requirement pair, and accepted-variant matrix for two positions.
        for (int stockA = 0; stockA <= 3; stockA++)
        for (int stockB = 0; stockB <= 3; stockB++)
        for (int requiredA = 1; requiredA <= 2; requiredA++)
        for (int requiredB = 1; requiredB <= 2; requiredB++)
        for (int maskA = 1; maskA <= 3; maskA++)
        for (int maskB = 1; maskB <= 3; maskB++)
        {
            var f = new AllocationFixture();
            var a = f.Stack(1, "board-a", stockA, 3);
            var b = f.Stack(2, "board-b", stockB, 3);
            CraftingRecipeIngredient Need(int mask, int quantity)
            {
                var need = f.Wildcard("board-*", quantity);
                need.AllowedVariants = mask == 1 ? new[] { "a" } : mask == 2 ? new[] { "b" } : new[] { "a", "b" };
                return need;
            }
            var result = f.Allocate(f.Recipe(Need(maskA, requiredA), Need(maskB, requiredB)), a, b);
            var best = (Coverage: -1, Executions: -1, Total: -1);
            for (int typeA = 0; typeA < 2; typeA++)
            for (int typeB = 0; typeB < 2; typeB++)
            for (int countA = 0; countA <= 3; countA++)
            for (int countB = 0; countB <= 3; countB++)
            {
                if ((countA > 0 && (maskA & (1 << typeA)) == 0)
                    || (countB > 0 && (maskB & (1 << typeB)) == 0)) continue;
                int usedA = (typeA == 0 ? countA : 0) + (typeB == 0 ? countB : 0);
                int usedB = (typeA == 1 ? countA : 0) + (typeB == 1 ? countB : 0);
                if (usedA > stockA || usedB > stockB) continue;
                var objective = (Math.Min(countA, requiredA) + Math.Min(countB, requiredB),
                    Math.Min(countA / requiredA, countB / requiredB), countA + countB);
                if (objective.CompareTo(best) > 0) best = objective;
            }
            Assert.True(result.Evaluation.IsSupported);
            Assert.Equal(best.Coverage, result.Evaluation.Allocated);
            Assert.Equal(best.Executions, result.CompleteExecutions);
            Assert.Equal(best.Total, result.Shift.Sum(placement => placement.Quantity));
            f.AssertConserved(result);
        }
    }
}
