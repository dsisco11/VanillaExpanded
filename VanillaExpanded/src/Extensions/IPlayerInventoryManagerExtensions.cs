using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded;
internal static class PlayerInventoryManagerExtensions
{
    /// <summary>
    /// Gets all item slots from all inventories which the player currently has opened.
    /// </summary>
    /// <param name="invManager"></param>
    /// <returns></returns>
    public static IEnumerable<ItemSlot> GetAccessibleSlots(this IPlayerInventoryManager invManager) => invManager.OpenedInventories.SelectMany(static inv => inv);

    /// <summary>
    /// Gets all item slots from all inventories which the player currently has opened.
    /// </summary>
    /// <param name="invManager"></param>
    /// <returns></returns>
    public static IEnumerable<ItemSlot> GetPlayerSlots(this IPlayerInventoryManager invManager) => invManager.InventoriesOrdered.SelectMany(static inv => inv);

    /// <summary>
    /// Gets the player's inventories which are "bag-like" and meant for general storage (such as backpack and hotbar).
    /// </summary>
    /// <param name="invManager"></param>
    /// <returns></returns>
    public static IEnumerable<IInventory> GetBagInventories(this IPlayerInventoryManager invManager) => invManager.GetInventories(GlobalConstants.backpackInvClassName, GlobalConstants.hotBarInvClassName);

    /// <summary>
    /// Gets multiple inventories by name, skipping any that are not found.
    /// </summary>
    public static IEnumerable<IInventory> GetInventories(this IPlayerInventoryManager invManager, params string[] inventoryNames)
    {
        foreach (var name in inventoryNames)
        {
            var inv = invManager.GetInventory(name);
            if (inv != null)
            {
                yield return inv;
            }
        }
    }

    /// <summary>
    /// Finds the best suited slot for the given source slot from multiple inventories.
    /// </summary>
    /// <param name="inventories"></param>
    /// <param name="sourceSlot"></param>
    /// <param name="op"></param>
    /// <param name="skipSlots"></param>
    /// <returns></returns>
    public static WeightedSlot? FindBestSlot(this IEnumerable<IInventory> inventories, ItemSlot sourceSlot, ItemStackMoveOperation? op = null, IEnumerable<ItemSlot>? skipSlots = null)
    {
        var skipSlotsList = skipSlots?.ToList() ?? [];
        return inventories.Select(inv => inv.GetBestSuitedSlot(sourceSlot, op, skipSlotsList))
            .Where(slot => slot?.slot is not null)
            .MaxBy(slot => slot!.weight);
    }

}
