using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Tracks the original item and temporary selection in the current client inventory view.</summary>
internal sealed class QuickToolSession
{
    private ItemStack? originalSnapshot;
    private ItemStack currentSnapshot;
    /// <summary>Starts a session after a successful first equip.</summary>
    internal QuickToolSession(int handPosition, ItemStack? original, string entryId, ItemStack current, QuickToolSlotAddress currentHome)
    {
        HandPosition = handPosition;
        Original = original;
        originalSnapshot = original?.Clone();
        OriginalQuantity = original?.StackSize ?? 0;
        CurrentEntryId = entryId;
        Current = current;
        currentSnapshot = current.Clone();
        CurrentQuantity = current.StackSize;
        CurrentHome = currentHome;
    }

    #region State
    /// <summary>Gets the original active hotbar position, independently of its current slot object.</summary>
    internal int HandPosition { get; }
    /// <summary>Gets the item displaced by the first selection, if any.</summary>
    internal ItemStack? Original { get; private set; }
    /// <summary>Gets the original whole-stack quantity.</summary>
    internal int OriginalQuantity { get; }
    /// <summary>Gets the currently selected semantic entry.</summary>
    internal string CurrentEntryId { get; private set; }
    /// <summary>Gets the current temporary item's local stack reference.</summary>
    internal ItemStack Current { get; private set; }
    /// <summary>Gets the current temporary item's whole-stack quantity.</summary>
    internal int CurrentQuantity { get; private set; }
    /// <summary>Gets the current temporary item's preferred return destination.</summary>
    internal QuickToolSlotAddress CurrentHome { get; private set; }

    /// <summary>Records the next temporary item and its home after native flips apply locally.</summary>
    internal void Selected(string entryId, ItemStack current, QuickToolSlotAddress currentHome)
    {
        CurrentEntryId = entryId;
        Current = current;
        currentSnapshot = current.Clone();
        CurrentQuantity = current.StackSize;
        CurrentHome = currentHome;
    }
    #endregion

    #region Action-time lookup
    /// <summary>Finds each exact stack once and validates the current hand without retaining its source location.</summary>
    internal bool TryResolve(QuickToolInventoryView view, ItemSlot physicalOffhand, out ItemSlot hand, out ItemSlot? originalAt)
    {
        hand = null!;
        originalAt = null;
        ItemSlot? currentAt = ResolveStack(view, physicalOffhand, Current, currentSnapshot, CurrentQuantity);
        if (currentAt is null || !ReferenceEquals(view.ActiveHand(), currentAt)
            || currentAt.Inventory.GetSlotId(currentAt) != HandPosition
            || !Usable(currentAt.Itemstack!, currentAt, CurrentEntryId)) return false;
        Current = currentAt.Itemstack!;
        currentSnapshot = Current.Clone();
        hand = currentAt;

        if (Original is null) return true;
        // Search current inventory rather than trusting A's previous slot; server synchronization may recreate its object.
        originalAt = ResolveStack(view, physicalOffhand, Original, originalSnapshot!, OriginalQuantity);
        if (originalAt is null || ReferenceEquals(originalAt, hand) || !Usable(originalAt.Itemstack!, originalAt, null)) return false;
        Original = originalAt.Itemstack;
        originalSnapshot = Original.Clone();
        return true;
    }

    /// <summary>Rebinds a server-recreated stack only when its complete saved contents identify one eligible slot.</summary>
    private static ItemSlot? ResolveStack(QuickToolInventoryView view, ItemSlot physicalOffhand,
        ItemStack tracked, ItemStack snapshot, int quantity)
    {
        ItemSlot? exact = view.Find(tracked, quantity, physicalOffhand);
        if (exact is not null) return exact;
        ItemSlot? match = null;
        foreach (ItemSlot slot in view.TrackedSlots(physicalOffhand))
        {
            ItemStack? candidate = slot.Itemstack;
            if (candidate is null || candidate.StackSize != quantity) continue;
            try
            {
                if (!snapshot.Equals(slot.Inventory.Api.World, candidate)) continue;
            }
            catch (Exception) { return null; }
            // Equal-looking duplicates cannot establish which physical stack the server returned.
            if (match is not null) return null;
            match = slot;
        }
        return match;
    }

    /// <summary>Rejects broken durable stacks and a current selection that ceased matching its entry.</summary>
    private static bool Usable(ItemStack stack, ItemSlot slot, string? entryId)
    {
        try
        {
            if (stack.Collectible is null) return false;
            int max = stack.Collectible.GetMaxDurability(stack);
            if (max > 0 && stack.Collectible.GetRemainingDurability(stack) <= 0) return false;
            if (entryId is null) return true;
            if (entryId == QuickToolLayout.LightId) return Lighting.LightSourceSelection.IsLightSource(stack.Collectible);
            return QuickToolLayout.TryGetToolTagsFromId(entryId, out _)
                || QuickToolLayout.TryGetTool(entryId, out EnumTool category) && stack.Collectible.GetTool(slot) == category;
        }
        catch (Exception) { return false; }
    }
    #endregion
}
