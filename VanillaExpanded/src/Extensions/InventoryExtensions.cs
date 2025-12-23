using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;

namespace VanillaExpanded;
internal static class InventoryExtensions
{
    /// <summary>
    /// Returns a set of all distinct item IDs in the given inventory.
    /// </summary>
    public static HashSet<int> GetDistinctItemIds(this IInventory inventory)
    {
        return [.. inventory.Where(static slot => !slot.Empty)
                            .Where(static slot => slot?.Itemstack?.Collectible?.Id is not null)
                            .Select(static slot => slot.Itemstack.Collectible.Id)];
    }

    /// <summary>
    /// Returns a set of all distinct item codes in the given inventory.
    /// </summary>
    public static HashSet<AssetLocation> GetDistinctItemCodes(this IInventory inventory)
    {
        return [.. inventory.Where(static slot => !slot.Empty)
                            .Where(static slot => slot?.Itemstack?.Collectible?.Code is not null)
                            .Select(static slot => slot.Itemstack.Collectible.Code)];
    }

    #region FindMatchingSlots
    /// <summary>
    /// Returns all item slots in the inventory that match the given item code.
    /// </summary>
    public static ImmutableArray<ItemSlot> FindMatchingSlots(this IInventory inventory, AssetLocation itemCode)
    {
        return inventory.Where(static slot => !slot.Empty)
                        .Where(slot => slot?.Itemstack?.Collectible?.Code == itemCode)
                        .ToImmutableArray();
    }

    /// <summary>
    /// Returns all item slots in the inventory that match the given item ID.
    /// </summary>
    public static ImmutableArray<ItemSlot> FindMatchingSlots(this IInventory inventory, int itemId)
    {
        return inventory.Where(static slot => !slot.Empty)
                        .Where(slot => slot?.Itemstack?.Collectible?.Id == itemId)
                        .ToImmutableArray();
    }

    /// <summary>
    /// Returns all item slots in the inventory that match the given item codes.
    /// </summary>
    public static ImmutableArray<ItemSlot> FindMatchingSlots(this IInventory inventory, HashSet<AssetLocation> itemCodes)
    {
        return inventory.Where(static slot => !slot.Empty)
                        .Where(slot => slot?.Itemstack?.Collectible?.Code is not null && itemCodes.Contains(slot.Itemstack.Collectible.Code))
                        .ToImmutableArray();
    }

    /// <summary>
    /// Returns all item slots in the inventory that match the given item IDs.
    /// </summary>
    public static ImmutableArray<ItemSlot> FindMatchingSlots(this IInventory inventory, HashSet<int> itemIds)
    {
        return inventory.Where(static slot => !slot.Empty)
                        .Where(slot => slot?.Itemstack?.Collectible?.Id is not null && itemIds.Contains(slot.Itemstack.Collectible.Id))
                        .ToImmutableArray();
    }
    #endregion
}
