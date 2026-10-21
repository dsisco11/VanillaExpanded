using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Assigns nonmergeable forms of the same metal to separate cooking slots before inventory transfers.</summary>
internal static class AlloyDepositStackPlanner
{
    #region Public API

    /// <summary>Selects concrete supplies, reserves their required slots, then spreads them into spare slots.</summary>
    internal static AlloyDepositPlan? Resolve(
        IWorldAccessor world,
        AlloyDepositPlan plan,
        IReadOnlyList<ItemSlot> cookingSlots,
        IEnumerable<ItemSlot> availableSlots,
        System.Func<ItemStack, MetalDepositIngredient, bool> matchesIngredient,
        bool spreadAcrossCookingSlots = false)
    {
        ItemStack[] available = availableSlots.Where(static slot => !slot.Empty)
            .Select(static slot => slot.Itemstack!)
            .OrderBy(static stack => stack.StackSize).ToArray();
        int[] slotIndices = spreadAcrossCookingSlots
            ? Enumerable.Range(0, cookingSlots.Count).ToArray()
            : plan.Targets.Select(static target => target.SlotIndex).Distinct().Order().ToArray();
        if (slotIndices.Length == 0) return null;
        int slotCapacity = slotIndices.Min(index => cookingSlots[index].MaxSlotStackSize);
        var forms = new List<(MetalDepositIngredient Ingredient, ItemStack Stack, int Amount, int Slots)>();
        foreach (var group in plan.Targets.GroupBy(static target => target.Ingredient))
        {
            var supplies = new List<(ItemStack Stack, int Amount)>();
            // Group compatible stacks while retaining the order of their smallest source stack.
            // Metal identity establishes eligibility, while stack equality establishes actual merge compatibility.
            foreach (ItemStack stack in available.Where(stack => matchesIngredient(stack, group.Key)))
            {
                int index = supplies.FindIndex(item => item.Stack.Equals(world, stack, GlobalConstants.IgnoredStackAttributes));
                if (index < 0) supplies.Add((stack, stack.StackSize));
                else supplies[index] = (supplies[index].Stack, supplies[index].Amount + stack.StackSize);
            }

            int remaining = group.Sum(static target => target.Amount);
            // Consume forms with the smallest source stacks first, reserving separate slots for each form.
            foreach (var supply in supplies)
            {
                int amount = Math.Min(remaining, supply.Amount);
                int capacity = Math.Min(slotCapacity, supply.Stack.Collectible.MaxStackSize);
                if (capacity <= 0) return null;
                int slots = (amount + capacity - 1) / capacity;
                forms.Add((group.Key, supply.Stack, amount, slots));
                remaining -= amount;
                if (remaining == 0) break;
            }
            if (remaining != 0) return null;
        }
        if (forms.Sum(static form => form.Slots) > slotIndices.Length) return null;

        // Execution retains a feasible layout, including deliberately uneven quantities, rather than reshuffling it.
        if (!spreadAcrossCookingSlots && plan.Targets.All(target => target.DesiredStack is not null
            && forms.Any(form => form.Ingredient == target.Ingredient
                && form.Stack.Equals(world, target.DesiredStack, GlobalConstants.IgnoredStackAttributes))
            && target.Amount <= Math.Min(slotCapacity, target.DesiredStack.Collectible.MaxStackSize))
            && forms.All(form => plan.Targets.Where(target => target.Ingredient == form.Ingredient
                && form.Stack.Equals(world, target.DesiredStack, GlobalConstants.IgnoredStackAttributes))
                .Sum(static target => target.Amount) == form.Amount))
        {
            return plan;
        }
        if (!spreadAcrossCookingSlots && forms.All(form =>
            forms.Count(other => other.Ingredient == form.Ingredient) == 1)
            && plan.Targets.All(target => target.Amount <= Math.Min(slotCapacity,
                forms.First(form => form.Ingredient == target.Ingredient).Stack.Collectible.MaxStackSize)))
        {
            return new AlloyDepositPlan(plan.Targets.Select(target => target with
            {
                DesiredStack = forms.First(form => form.Ingredient == target.Ingredient).Stack.Clone()
            }).ToImmutableArray());
        }

        // Only slots left after every selected form fits are available for balancing.
        int usedSlots = forms.Sum(static form => form.Slots);
        while (usedSlots < slotIndices.Length)
        {
            int index = Enumerable.Range(0, forms.Count)
                .Where(index => forms[index].Slots < forms[index].Amount)
                .OrderByDescending(index => (double)forms[index].Amount / forms[index].Slots)
                .DefaultIfEmpty(-1).First();
            if (index < 0) break;
            var form = forms[index];
            forms[index] = (form.Ingredient, form.Stack, form.Amount, form.Slots + 1);
            usedSlots++;
        }
        var targets = ImmutableArray.CreateBuilder<AlloyDepositSlotTarget>();
        int targetIndex = 0;
        foreach (var form in forms)
        {
            for (int offset = 0; offset < form.Slots; offset++)
            {
                int amount = form.Amount / form.Slots + (offset < form.Amount % form.Slots ? 1 : 0);
                targets.Add(new AlloyDepositSlotTarget(
                    slotIndices[targetIndex++], form.Ingredient, amount, form.Stack.Clone()));
            }
        }
        return new AlloyDepositPlan(targets.ToImmutable());
    }

    #endregion
}
