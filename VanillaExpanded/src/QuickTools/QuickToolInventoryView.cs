using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.QuickTools;

/// <summary>Resolves supported player-owned slots before client native inventory flips.</summary>
internal sealed class QuickToolInventoryView
{
    private const string HotbarType = "Vintagestory.Common.InventoryPlayerHotbar";
    private const string BackpackType = "Vintagestory.Common.InventoryPlayerBackpacks";
    private readonly IPlayerInventoryManager manager;
    private readonly bool allowGenericFixture;

    /// <summary>Captures the current manager; inventory objects and slot topology are always read again.</summary>
    internal QuickToolInventoryView(IPlayerInventoryManager manager, bool allowGenericFixture = false)
    {
        this.manager = manager;
        this.allowGenericFixture = allowGenericFixture;
    }

    #region Owned locations
    /// <summary>Resolves a home address against current supported inventory instances and slot topology.</summary>
    internal ItemSlot? Resolve(QuickToolSlotAddress address, ItemSlot physicalOffhand)
    {
        if (ReferenceEquals(address.Inventory, Hotbar()))
            return GetSource(false, address.Index, physicalOffhand, true);
        if (ReferenceEquals(address.Inventory, Backpack()))
            return GetSource(true, address.Index, physicalOffhand, false);
        return null;
    }

    /// <summary>Returns the current supported active hand, or null if its inventory adapter changed.</summary>
    internal ItemSlot? ActiveHand()
    {
        IInventory? hotbar = Hotbar();
        ItemSlot? hand = manager.ActiveHotbarSlot;
        return hotbar is not null && hand?.GetType() == typeof(ItemSlotSurvival) && Contains(hotbar, hand) ? hand : null;
    }

    /// <summary>Returns the physical offhand only when it is the supported hotbar slot object.</summary>
    internal ItemSlot? Offhand(ItemSlot physicalOffhand)
    {
        IInventory? hotbar = Hotbar();
        return hotbar is not null && physicalOffhand is ItemSlotOffhand && Contains(hotbar, physicalOffhand)
            && ReferenceEquals(manager.OffhandHotbarSlot, physicalOffhand) ? physicalOffhand : null;
    }

    /// <summary>Gets an exact supported slot address, optionally admitting physical offhand for a light source.</summary>
    internal ItemSlot? GetSource(bool backpack, int index, ItemSlot physicalOffhand, bool allowOffhand)
    {
        IInventory? inventory = backpack ? Backpack() : Hotbar();
        if (inventory is null || index < 0 || index >= inventory.Count) return null;
        ItemSlot? slot = inventory[index];
        if (slot is null || !ReferenceEquals(slot.Inventory, inventory)) return null;
        if (backpack) return slot is ItemSlotBagContent ? slot : null;
        if (slot.GetType() == typeof(ItemSlotSurvival)) return slot;
        return allowOffhand && ReferenceEquals(slot, Offhand(physicalOffhand)) ? slot : null;
    }

    /// <summary>Enumerates supported ordinary storage in deterministic hotbar then backpack order.</summary>
    internal IEnumerable<ItemSlot> OrdinarySlots()
    {
        IInventory? hotbar = Hotbar();
        if (hotbar is not null)
            for (int i = 0; i < hotbar.Count; i++)
                if (GetSource(false, i, null!, false) is ItemSlot slot) yield return slot;
        IInventory? backpack = Backpack();
        if (backpack is not null)
            for (int i = 0; i < backpack.Count; i++)
                if (GetSource(true, i, null!, false) is ItemSlot slot) yield return slot;
    }

    /// <summary>Enumerates ordinary storage and the physical offhand for identity continuity.</summary>
    internal IEnumerable<ItemSlot> TrackedSlots(ItemSlot physicalOffhand)
    {
        foreach (ItemSlot slot in OrdinarySlots()) yield return slot;
        if (Offhand(physicalOffhand) is ItemSlot offhand) yield return offhand;
    }

    /// <summary>Finds exactly one current slot with the same local stack reference and quantity.</summary>
    internal ItemSlot? Find(ItemStack stack, int quantity, ItemSlot physicalOffhand)
    {
        ItemSlot? found = null;
        foreach (ItemSlot slot in TrackedSlots(physicalOffhand))
        {
            if (!ReferenceEquals(slot.Itemstack, stack)) continue;
            if (stack.StackSize != quantity || found is not null) return null;
            found = slot;
        }
        return found;
    }

    /// <summary>Checks that an existing slot object is still an eligible member of the current topology.</summary>
    internal bool ContainsTracked(ItemSlot slot, ItemSlot physicalOffhand)
    {
        foreach (ItemSlot candidate in TrackedSlots(physicalOffhand)) if (ReferenceEquals(candidate, slot)) return true;
        return false;
    }
    #endregion

    #region Inventory compatibility
    /// <summary>Gets the current built-in hotbar adapter.</summary>
    private IInventory? Hotbar() => Compatible(manager.GetOwnInventory(GlobalConstants.hotBarInvClassName), HotbarType);

    /// <summary>Gets the current built-in backpack adapter.</summary>
    private IInventory? Backpack() => Compatible(manager.GetOwnInventory(GlobalConstants.backpackInvClassName), BackpackType);

    /// <summary>Rejects unverified custom inventory persistence implementations before movement.</summary>
    private IInventory? Compatible(IInventory? inventory, string runtimeType)
    {
        if (inventory is not InventoryBase) return null;
        Type type = inventory.GetType();
        return type.FullName == runtimeType || (allowGenericFixture && type == typeof(InventoryGeneric)) ? inventory : null;
    }

    /// <summary>Checks slot object identity without trusting a stale numerical address.</summary>
    private static bool Contains(IInventory inventory, ItemSlot slot)
    {
        for (int i = 0; i < inventory.Count; i++) if (ReferenceEquals(inventory[i], slot)) return true;
        return false;
    }
    #endregion
}
