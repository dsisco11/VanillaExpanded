using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Lighting;

/// <summary>Resolves the player's preferred existing light without moving any inventory contents.</summary>
public static class LightSourceSelection
{
    #region Selection
    /// <summary>Returns the legacy offhand, active hand, hotbar, then backpack choice.</summary>
    public static ItemSlot? ResolveLightSourceSlot(IPlayerInventoryManager playerInventory, ItemSlot offhandSlot, out bool isInLeftHand, out bool isInRightHand)
    {
        isInLeftHand = IsLightSource(offhandSlot);
        isInRightHand = false;
        if (isInLeftHand) return offhandSlot;

        ItemSlot active = playerInventory.ActiveHotbarSlot;
        isInRightHand = IsLightSource(active);
        if (isInRightHand) return active;

        // The first inventory with a light wins, even if a later inventory has a brighter one.
        IInventory? hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        if (hotbar is not null && TryFindBrightestLightSource(hotbar, out ItemSlot? hotbarSlot)) return hotbarSlot;
        IInventory? backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        return backpack is not null && TryFindBrightestLightSource(backpack, out ItemSlot? backpackSlot) ? backpackSlot : null;
    }

    /// <summary>Returns the brightest slot in inventory order, keeping the first on equal brightness.</summary>
    public static bool TryFindBrightestLightSource(IInventory inventory, [NotNullWhen(true)] out ItemSlot? result)
    {
        result = null;
        int brightness = 0;
        foreach (ItemSlot slot in inventory)
        {
            int level = slot.Empty ? 0 : slot.Itemstack.Collectible.LightHsv[2];
            if (level <= brightness) continue;
            brightness = level;
            result = slot;
        }
        return result is not null;
    }

    /// <summary>Reports whether a slot contains a collectible with positive light value.</summary>
    public static bool IsLightSource(ItemSlot? slot) => slot is not null && !slot.Empty && IsLightSource(slot.Itemstack.Collectible);

    /// <summary>Reports whether a collectible has positive light value.</summary>
    public static bool IsLightSource(CollectibleObject? item) => (item?.LightHsv[2] ?? 0) > 0;
    #endregion
}
