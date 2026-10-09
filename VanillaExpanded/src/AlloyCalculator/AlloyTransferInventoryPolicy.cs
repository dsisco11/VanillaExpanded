using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Defines inventory selection, ordering, and directional permissions for alloy transfers.</summary>
internal static class AlloyTransferInventoryPolicy
{
    #region Public API

    /// <summary>Collects backpack, hotbar, then opened storage slots; missing player inventories produce an empty failed result.</summary>
    internal static bool TryCollect(
        IWorldAccessor world,
        IPlayerInventoryManager manager,
        IInventory target,
        out IReadOnlyList<ItemSlot> slots)
    {
        slots = [];
        IInventory? backpack = manager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbar = manager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (backpack is null || hotbar is null) return false;

        // The opened list defines this operation's order. Never include the target or enumerate it as storage.
        var inventories = new HashSet<IInventory>(System.Collections.Generic.ReferenceEqualityComparer.Instance) { target };
        var seenSlots = new HashSet<ItemSlot>(target, System.Collections.Generic.ReferenceEqualityComparer.Instance);
        var collected = new List<ItemSlot>();
        AppendSlots(backpack, inventories, seenSlots, collected);
        AppendSlots(hotbar, inventories, seenSlots, collected);
        foreach (IInventory inventory in manager.OpenedInventories.ToArray())
        {
            if (!inventories.Contains(inventory) && IsOpenedStorage(world, inventory))
            {
                AppendSlots(inventory, inventories, seenSlots, collected);
            }
        }

        slots = collected;
        return true;
    }

    /// <summary>Checks live withdrawal eligibility, including specialized slot and inventory take restrictions.</summary>
    internal static bool CanWithdraw(ItemSlot slot)
    {
        return !slot.Empty && slot.CanTake();
    }

    /// <summary>Checks live return compatibility and containment without predicting or performing a transfer.</summary>
    internal static bool CanReturnTo(ItemSlot destination, ItemSlot source)
    {
        return !ReferenceEquals(destination, source)
            && CanWithdraw(source)
            && destination.CanTakeFrom(source)
            && destination.CanHold(source)
            && destination.Inventory?.CanContain(destination, source) != false;
    }

    /// <summary>Checks that an external slot still belongs to a current player inventory or opened supported storage owner.</summary>
    internal static bool IsCurrentExternalSlot(
        IWorldAccessor world,
        IPlayerInventoryManager manager,
        IInventory target,
        ItemSlot slot)
    {
        IInventory inventory = slot.Inventory;
        if (inventory is null || ReferenceEquals(inventory, target)
            || !inventory.Any(candidate => ReferenceEquals(candidate, slot))) return false;

        // Resolve ownership at movement time so closing or replacing a container cannot leave stale eligible slots.
        if (ReferenceEquals(inventory, manager.GetOwnInventory(GlobalConstants.backpackInvClassName))
            || ReferenceEquals(inventory, manager.GetOwnInventory(GlobalConstants.hotBarInvClassName))) return true;
        return manager.OpenedInventories.Any(candidate => ReferenceEquals(candidate, inventory))
            && IsOpenedStorage(world, inventory);
    }

    #endregion

    #region Private

    /// <summary>Accepts evidenced storage owner families only when the owner still holds this exact inventory.</summary>
    private static bool IsOpenedStorage(IWorldAccessor world, IInventory inventory)
    {
        if (inventory is not InventoryBase { Pos: not null } positioned) return false;

        // Generic inventories also back machines. Resolve the actual owner instead of guessing from inventory names.
        BlockEntity? owner = world.BlockAccessor.GetBlockEntity(positioned.Pos);
        return owner is BlockEntityContainer container
            && owner is BlockEntityGenericContainer or BlockEntityGenericTypedContainer or BlockEntityCrate
            && ReferenceEquals(container.Inventory, inventory);
    }

    /// <summary>Appends each inventory and slot identity once while retaining enumeration order.</summary>
    private static void AppendSlots(
        IInventory inventory,
        HashSet<IInventory> inventories,
        HashSet<ItemSlot> seenSlots,
        List<ItemSlot> collected)
    {
        if (!inventories.Add(inventory)) return;
        foreach (ItemSlot slot in inventory)
        {
            if (seenSlots.Add(slot)) collected.Add(slot);
        }
    }

    #endregion
}
