using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Chooses selection destinations and native flip order without changing inventory or session history.</summary>
internal static class QuickToolSelectionPlanner
{
    #region Public API
    /// <summary>Prefers a direct exchange, then routes an incompatible bag displacement to compatible free storage.</summary>
    internal static QuickToolMovementPlan? TryCreate(QuickToolInventoryView view, ItemSlot physicalOffhand,
        ItemSlot hand, QuickToolCandidate candidate, ItemStack? original,
        ItemSlot? originalAt, ItemSlot? returnSlot, out List<ItemSlot> targets)
    {
        var desired = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance)
        {
            [hand] = candidate.Stack,
            [candidate.Slot] = original
        };
        if (returnSlot is not null) desired[returnSlot] = hand.Itemstack;
        if (originalAt is not null && !ReferenceEquals(originalAt, returnSlot)) desired[originalAt] = null;
        targets = [];
        if (returnSlot is not null) targets.Add(returnSlot);
        if (originalAt is not null && !ReferenceEquals(originalAt, returnSlot)) targets.Add(originalAt);
        targets.Add(candidate.Slot);
        QuickToolMovementPlan? direct = QuickToolMovementPlan.TryCreate(view, physicalOffhand, desired);
        if (direct is not null) return direct;

        // Bag contents remain eligible sources even when the displaced item cannot be stored there.
        // Offhand retains its separate direct-exchange contract.
        if (candidate.Slot is not ItemSlotBagContent || original is null) return null;
        bool retainOriginal = originalAt is not null && !ReferenceEquals(originalAt, returnSlot);
        IEnumerable<ItemSlot> deposits = retainOriginal ? new[] { originalAt! } : view.OrdinarySlots();
        foreach (ItemSlot deposit in deposits)
        {
            if (ReferenceEquals(deposit, hand) || ReferenceEquals(deposit, candidate.Slot)
                || ReferenceEquals(deposit, returnSlot) || (!retainOriginal && !deposit.Empty)) continue;
            var fallback = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance)
            {
                [hand] = candidate.Stack,
                [candidate.Slot] = null,
                [deposit] = original
            };
            if (returnSlot is not null) fallback[returnSlot] = hand.Itemstack;
            QuickToolMovementPlan? plan = QuickToolMovementPlan.TryCreate(view, physicalOffhand, fallback);
            if (plan is null) continue;
            // Returning the previous tool leaves H empty unless its home holds A. In that case,
            // move A out first; otherwise leave its already safe location untouched.
            targets = [];
            if (returnSlot is not null) targets.Add(returnSlot);
            if (!retainOriginal) targets.Add(deposit);
            targets.Add(candidate.Slot);
            return plan;
        }
        return null;
    }
    #endregion
}
