using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Prechecks an intended arrangement and executes its ordinary inventory flips in order.</summary>
internal sealed class QuickToolMovementPlan
{
    private readonly Assignment[] snapshot;
    private readonly QuickToolInventoryView view;
    private readonly ItemSlot offhand;

    /// <summary>Retains the unchanged slot snapshot before any native flip is attempted.</summary>
    private QuickToolMovementPlan(QuickToolInventoryView view, ItemSlot offhand, Assignment[] snapshot)
    {
        this.view = view;
        this.offhand = offhand;
        this.snapshot = snapshot;
    }

    #region Planning
    /// <summary>Checks that final destinations conserve the same exact stack references and accept them.</summary>
    internal static QuickToolMovementPlan? TryCreate(QuickToolInventoryView view, ItemSlot offhand, IReadOnlyDictionary<ItemSlot, ItemStack?> desired)
    {
        if (desired.Count is < 2 or > 5) return null;
        var previous = new Dictionary<ItemSlot, ItemStack?>(ReferenceEqualityComparer.Instance);
        foreach (var pair in desired)
        {
            if (pair.Key is null || !view.ContainsTracked(pair.Key, offhand)) return null;
            previous.Add(pair.Key, pair.Key.Itemstack);
        }

        // Check the whole final arrangement before the first native packet, without promising atomicity.
        var sources = new Dictionary<ItemStack, ItemSlot>(ReferenceEqualityComparer.Instance);
        foreach (var pair in previous)
            if (pair.Value is ItemStack stack && !sources.TryAdd(stack, pair.Key)) return null;
        var assigned = new HashSet<ItemStack>(ReferenceEqualityComparer.Instance);
        foreach (var pair in desired)
            if (pair.Value is ItemStack stack && (!sources.ContainsKey(stack) || !assigned.Add(stack))) return null;
        if (assigned.Count != sources.Count) return null;

        var entries = new List<Assignment>(desired.Count);
        bool changes = false;
        try
        {
            foreach (var pair in desired)
            {
                ItemSlot slot = pair.Key;
                ItemStack? before = previous[slot];
                ItemStack? after = pair.Value;
                entries.Add(new Assignment(slot, before, after, before?.StackSize ?? 0, after?.StackSize ?? 0));
                if (ReferenceEquals(before, after)) continue;
                changes = true;
                if (before is not null && !slot.CanTake()) return null;
                if (after is not null && (after.StackSize <= 0 || after.StackSize > slot.MaxSlotStackSize
                    || !slot.CanHold(sources[after]))) return null;
            }
        }
        catch (Exception) { return null; }

        var plan = new QuickToolMovementPlan(view, offhand, [.. entries]);
        return changes && plan.IsOriginalSnapshotCurrent() ? plan : null;
    }

    /// <summary>Rejects a changed topology, reference, or quantity before the first flip.</summary>
    private bool IsOriginalSnapshotCurrent()
    {
        foreach (Assignment entry in snapshot)
            if (!view.ContainsTracked(entry.Slot, offhand)
                || !ReferenceEquals(entry.Slot.Itemstack, entry.Before)
                || (entry.Before is not null && entry.Before.StackSize != entry.BeforeQuantity)) return false;
        return true;
    }
    #endregion

    #region Native execution
    /// <summary>Performs each client flip and sends its exact game packet, stopping on the first uncertainty.</summary>
    internal QuickToolMovementStatus Execute(ItemSlot hand, IReadOnlyList<ItemSlot> targets, Action<object> send, Func<bool>? canContinue = null)
    {
        if (canContinue?.Invoke() == false || !IsOriginalSnapshotCurrent() || targets.Count == 0 || !ReferenceEquals(view.ActiveHand(), hand))
            return QuickToolMovementStatus.Rejected;

        var expected = new Dictionary<ItemSlot, (ItemStack? Stack, int Quantity)>(ReferenceEqualityComparer.Instance);
        foreach (Assignment entry in snapshot) expected.Add(entry.Slot, (entry.Before, entry.BeforeQuantity));
        int sent = 0;
        foreach (ItemSlot target in targets)
        {
            if (canContinue?.Invoke() == false || !ReferenceEquals(view.ActiveHand(), hand) || !view.ContainsTracked(target, offhand)
                || ReferenceEquals(hand, target) || !expected.TryGetValue(hand, out var expectedHand)
                || !expected.TryGetValue(target, out var expectedTarget))
                return sent == 0 ? QuickToolMovementStatus.Rejected : QuickToolMovementStatus.Interrupted;
            ItemStack? beforeHand = hand.Itemstack;
            ItemStack? beforeTarget = target.Itemstack;
            int handQuantity = beforeHand?.StackSize ?? 0;
            int targetQuantity = beforeTarget?.StackSize ?? 0;
            if (!ReferenceEquals(beforeHand, expectedHand.Stack) || handQuantity != expectedHand.Quantity
                || !ReferenceEquals(beforeTarget, expectedTarget.Stack) || targetQuantity != expectedTarget.Quantity)
                return sent == 0 ? QuickToolMovementStatus.Rejected : QuickToolMovementStatus.Interrupted;
            IInventory? targetInventory = target.Inventory;
            int targetIndex = targetInventory?.GetSlotId(target) ?? -1;
            if (targetInventory is null || targetIndex < 0)
                return sent == 0 ? QuickToolMovementStatus.Rejected : QuickToolMovementStatus.Interrupted;

            try
            {
                object? packet = targetInventory.TryFlipItems(targetIndex, hand);
                if (packet is null)
                    return ReferenceEquals(hand.Itemstack, beforeHand) && ReferenceEquals(target.Itemstack, beforeTarget)
                        ? (sent == 0 ? QuickToolMovementStatus.Rejected : QuickToolMovementStatus.Interrupted)
                        : QuickToolMovementStatus.Interrupted;
                send(packet);
                sent++;
                // Native flip callbacks may run mod code; verify the expected local references before another packet.
                if (!ReferenceEquals(hand.Itemstack, beforeTarget) || !ReferenceEquals(target.Itemstack, beforeHand)
                    || (hand.Itemstack?.StackSize ?? 0) != targetQuantity
                    || (target.Itemstack?.StackSize ?? 0) != handQuantity)
                    return QuickToolMovementStatus.Interrupted;
                expected[hand] = expectedTarget;
                expected[target] = expectedHand;
                hand.MarkDirty();
                target.MarkDirty();
                // A native callback may end the session lifetime; finish this packet but stop further flips.
                if (canContinue?.Invoke() == false) return QuickToolMovementStatus.Interrupted;
            }
            catch (Exception)
            {
                // A callback or transport may fail after the local flip; retain actual contents for reconciliation.
                return QuickToolMovementStatus.Interrupted;
            }
        }

        foreach (Assignment entry in snapshot)
            if (!ReferenceEquals(entry.Slot.Itemstack, entry.After)
                || (entry.Slot.Itemstack?.StackSize ?? 0) != entry.AfterQuantity) return QuickToolMovementStatus.Interrupted;
        return QuickToolMovementStatus.LocallyApplied;
    }
    #endregion

    /// <summary>Captures a before/after location and quantity without cloning an item stack.</summary>
    private sealed record Assignment(ItemSlot Slot, ItemStack? Before, ItemStack? After, int BeforeQuantity, int AfterQuantity);
}

/// <summary>Distinguishes unchanged preflight rejection from a provisional or interrupted native sequence.</summary>
internal enum QuickToolMovementStatus
{
    Rejected,
    LocallyApplied,
    Interrupted
}
