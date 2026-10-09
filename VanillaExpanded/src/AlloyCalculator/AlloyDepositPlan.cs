using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

internal sealed record AlloyDepositPlan(ImmutableArray<AlloyDepositSlotTarget> Targets)
{
    internal static AlloyDepositPlan? Create(
        IReadOnlyList<MetalDepositIngredient> ingredients,
        IReadOnlyDictionary<int, ItemStack> calculatedStacks,
        int cookingSlotCount)
    {
        if (cookingSlotCount <= 0
            || ingredients.Count == 0
            || ingredients.Where((_, index) => !calculatedStacks.TryGetValue(index, out ItemStack? stack)
                || stack.StackSize <= 0).Any())
        {
            return null;
        }

        var amounts = ingredients
            .Select((ingredient, index) => (Ingredient: ingredient, Amount: calculatedStacks[index].StackSize))
            .OrderByDescending(static item => item.Amount)
            .ToList();
        int[] allocations = AlloyCalculatorLogic.AllocateSlotsProportionally(
            amounts.Select(static item => item.Amount).ToArray(),
            cookingSlotCount);
        var targets = ImmutableArray.CreateBuilder<AlloyDepositSlotTarget>();
        int slotIndex = 0;

        for (int ingredientIndex = 0; ingredientIndex < amounts.Count; ingredientIndex++)
        {
            var item = amounts[ingredientIndex];
            int allocatedSlots = allocations[ingredientIndex];
            int itemsPerSlot = item.Amount / allocatedSlots;
            int remainder = item.Amount % allocatedSlots;

            for (int offset = 0; offset < allocatedSlots; offset++, slotIndex++)
            {
                int slotAmount = itemsPerSlot + (offset < remainder ? 1 : 0);
                if (slotAmount > 0)
                {
                    targets.Add(new AlloyDepositSlotTarget(slotIndex, item.Ingredient, slotAmount));
                }
            }
        }

        return new AlloyDepositPlan(targets.ToImmutable());
    }
}