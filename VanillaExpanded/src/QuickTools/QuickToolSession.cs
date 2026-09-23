using System;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Tracks the original item and temporary selection in the current client inventory view.</summary>
internal sealed class QuickToolSession
{
    /// <summary>Starts a session after a successful first equip.</summary>
    internal QuickToolSession(int handPosition, ItemStack? original, string entryId, ItemStack current, QuickToolSlotAddress currentHome)
    {
        HandPosition = handPosition;
        Original = original;
        OriginalQuantity = original?.StackSize ?? 0;
        CurrentEntryId = entryId;
        Current = current;
        CurrentQuantity = current.StackSize;
        CurrentHome = currentHome;
    }

    #region State
    /// <summary>Gets the original active hotbar position, independently of its current slot object.</summary>
    internal int HandPosition { get; }
    /// <summary>Gets the item displaced by the first selection, if any.</summary>
    internal ItemStack? Original { get; }
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
        ItemSlot? currentAt = view.Find(Current, CurrentQuantity, physicalOffhand);
        if (currentAt is null || !ReferenceEquals(view.ActiveHand(), currentAt)
            || currentAt.Inventory.GetSlotId(currentAt) != HandPosition
            || !Usable(Current, currentAt, CurrentEntryId)) return false;
        hand = currentAt;

        if (Original is null) return true;
        // A may have moved without any callback. Its reference, rather than its previous slot, identifies it.
        originalAt = view.Find(Original, OriginalQuantity, physicalOffhand);
        return originalAt is not null && !ReferenceEquals(originalAt, hand) && Usable(Original, originalAt, null);
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
            return QuickToolLayout.TryGetTool(entryId, out EnumTool category)
                && stack.Collectible.GetTool(slot) == category;
        }
        catch (Exception) { return false; }
    }
    #endregion
}
