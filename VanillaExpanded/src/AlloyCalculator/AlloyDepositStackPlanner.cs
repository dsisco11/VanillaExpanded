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

    /// <summary>Resolves ingredient totals into available stack forms within their allocated cooking slots.</summary>
    internal static AlloyDepositPlan? Resolve(
        IWorldAccessor world,
        AlloyDepositPlan plan,
        IReadOnlyList<ItemSlot> cookingSlots,
        IEnumerable<ItemSlot> availableSlots,
        System.Func<ItemStack, MetalDepositIngredient, bool> matchesIngredient)
    {
        ItemStack[] available = availableSlots.Where(static slot => !slot.Empty)
            .Select(static slot => slot.Itemstack!)
            .OrderBy(static stack => stack.StackSize).ToArray();
        var targets = ImmutableArray.CreateBuilder<AlloyDepositSlotTarget>();
        foreach (var group in plan.Targets.GroupBy(static target => target.Ingredient))
        {
            AlloyDepositSlotTarget[] allocated = group.OrderBy(static target => target.SlotIndex).ToArray();
            var supplies = new List<(ItemStack Stack, int Amount)>();
            // Group compatible stacks while retaining the order of their smallest source stack.
            // Metal identity establishes eligibility, while stack equality establishes actual merge compatibility.
            foreach (ItemStack stack in available.Where(stack => matchesIngredient(stack, group.Key)))
            {
                int index = supplies.FindIndex(item => item.Stack.Equals(world, stack, GlobalConstants.IgnoredStackAttributes));
                if (index < 0) supplies.Add((stack, stack.StackSize));
                else supplies[index] = (supplies[index].Stack, supplies[index].Amount + stack.StackSize);
            }

            int remaining = allocated.Sum(static target => target.Amount);
            int slotCapacity = allocated.Min(target => cookingSlots[target.SlotIndex].MaxSlotStackSize);
            var forms = new List<(ItemStack Stack, int Amount, int Slots)>();
            // Consume forms with the smallest source stacks first, reserving separate slots for each form.
            foreach (var supply in supplies)
            {
                int amount = Math.Min(remaining, supply.Amount);
                int capacity = Math.Min(slotCapacity, supply.Stack.Collectible.MaxStackSize);
                if (capacity <= 0) return null;
                int slots = (amount + capacity - 1) / capacity;
                forms.Add((supply.Stack, amount, slots));
                remaining -= amount;
                if (remaining == 0) break;
            }
            if (remaining != 0 || forms.Sum(static form => form.Slots) > allocated.Length) return null;

            // A single form can retain the original layout, including deliberate uneven slot quantities.
            if (forms.Count == 1 && allocated.All(target => target.Amount
                <= Math.Min(slotCapacity, forms[0].Stack.Collectible.MaxStackSize)))
            {
                foreach (AlloyDepositSlotTarget target in allocated)
                    targets.Add(target with { DesiredStack = forms[0].Stack.Clone() });
                continue;
            }

            // Spread each form over spare slots without combining incompatible forms or exceeding stack limits.
            while (forms.Sum(static form => form.Slots) < allocated.Length)
            {
                int index = Enumerable.Range(0, forms.Count)
                    .Where(index => forms[index].Slots < forms[index].Amount)
                    .OrderByDescending(index => (double)forms[index].Amount / forms[index].Slots)
                    .DefaultIfEmpty(-1).First();
                if (index < 0) break;
                var form = forms[index];
                forms[index] = (form.Stack, form.Amount, form.Slots + 1);
            }
            int targetIndex = 0;
            foreach (var form in forms)
            {
                for (int offset = 0; offset < form.Slots; offset++)
                {
                    int amount = form.Amount / form.Slots + (offset < form.Amount % form.Slots ? 1 : 0);
                    targets.Add(new AlloyDepositSlotTarget(
                        allocated[targetIndex++].SlotIndex, group.Key, amount, form.Stack.Clone()));
                }
            }
        }
        return new AlloyDepositPlan(targets.ToImmutable());
    }

    #endregion
}
