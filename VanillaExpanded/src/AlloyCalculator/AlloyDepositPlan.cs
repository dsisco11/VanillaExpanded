using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

internal sealed record AlloyDepositPlan(ImmutableArray<AlloyDepositSlotTarget> Targets)
{
    /// <summary>Captures ingredient totals without allocating space before concrete stack forms are selected.</summary>
    internal static AlloyDepositPlan? CreateRequest(
        IReadOnlyList<MetalDepositIngredient> ingredients,
        IReadOnlyDictionary<int, ItemStack> calculatedStacks,
        int cookingSlotCount)
    {
        if (cookingSlotCount <= 0
            || ingredients.Count == 0
            || ingredients.Count > cookingSlotCount
            || ingredients.Where((_, index) => !calculatedStacks.TryGetValue(index, out ItemStack? stack)
                || stack.StackSize <= 0).Any())
        {
            return null;
        }

        var amounts = ingredients
            .Select((ingredient, index) => (Ingredient: ingredient, Amount: calculatedStacks[index].StackSize))
            .OrderByDescending(static item => item.Amount)
            .ToList();
        var targets = ImmutableArray.CreateBuilder<AlloyDepositSlotTarget>();
        for (int ingredientIndex = 0; ingredientIndex < amounts.Count; ingredientIndex++)
        {
            var item = amounts[ingredientIndex];
            targets.Add(new AlloyDepositSlotTarget(ingredientIndex, item.Ingredient, item.Amount));
        }

        return new AlloyDepositPlan(targets.ToImmutable());
    }
}