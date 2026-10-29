using System;
using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingRequirement
{
    private readonly CraftingRecipeIngredient ingredient;

    internal CraftingRequirement(int position, string id, CraftingRecipeIngredient ingredient)
    {
        GridPosition = position;
        Id = id;
        this.ingredient = ingredient.Clone();
        IsConsumed = ingredient.GetConsumeProperties().Consume;
        Quantity = IsConsumed ? ingredient.Quantity : 1;
    }

    internal int GridPosition { get; }
    internal string Id { get; }
    internal int Quantity { get; }
    internal bool IsConsumed { get; }
    internal CraftingRecipeIngredient GetIngredient() => ingredient.Clone();
}

internal sealed record CraftingSourceAllocation(CraftingSlotId Slot, int Quantity);

internal sealed class CraftingPlacement
{
    private readonly ItemStack stack;

    internal CraftingPlacement(int gridPosition, ItemStack stack, ImmutableArray<CraftingSourceAllocation> sources)
    {
        GridPosition = gridPosition;
        this.stack = CraftingStackSnapshot.Copy(stack);
        Sources = sources;
    }

    internal int GridPosition { get; }
    internal int Quantity => stack.StackSize;
    internal ImmutableArray<CraftingSourceAllocation> Sources { get; }
    internal ItemStack GetStack() => CraftingStackSnapshot.Copy(stack);
}

// CompleteExecutions is unbounded (null) only for supported layouts with no depleting requirement.
internal sealed record CraftingAllocation(
    CraftingRecipeEvaluation Evaluation,
    ImmutableArray<CraftingRequirement> Requirements,
    ImmutableArray<CraftingPlacement> Ordinary,
    ImmutableArray<CraftingPlacement> Shift,
    int? CompleteExecutions)
{
    internal static CraftingAllocation Unsupported(string reason) => new(
        CraftingRecipeEvaluation.Unsupported(reason), ImmutableArray<CraftingRequirement>.Empty,
        ImmutableArray<CraftingPlacement>.Empty, ImmutableArray<CraftingPlacement>.Empty, null);
}
