using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.InputHandlers;

internal record struct SlotId(string? InvClassName = null, int? SlotIndex = null);
internal static class EquipLightSource
{
    public static SlotId lastSwappedSlot;
    internal static bool OnHotKeyPressed(in ICoreClientAPI client, KeyCombination t1, bool toOffhand)
    {
        IClientPlayer player = client.World.Player;
        IPlayerInventoryManager playerInventory = player.InventoryManager;

        GetSlotsToFlip(client, t1, toOffhand, out ItemSlot? sourceSlot, out ItemSlot? targetSlot);
        if (sourceSlot is null)
        {// no suitable light source found to equip
            return false;
        }

        if (lastSwappedSlot.SlotIndex is not null)
        {
            lastSwappedSlot = new();// clear the tracking var for last swapped slot
        }
        else
        {
            lastSwappedSlot = new SlotId(sourceSlot.Inventory?.ClassName, sourceSlot.Inventory?.GetSlotId(sourceSlot));
        }

        if (sourceSlot is null)
        {
            client.Logger.Warning("[EquipLightSource] cannot swap item-slots, source-slot is null.");
            return false;
        }

        if (targetSlot is null)
        {
            client.Logger.Warning("[EquipLightSource] cannot swap item-slots, target-slot is null.");
            return false;
        }

        int targetSlotId = targetSlot.Inventory.GetSlotId(targetSlot);
        var packet = targetSlot.Inventory.TryFlipItems(targetSlotId, sourceSlot);
        if (packet is not null)
        {
            client.Network.SendPacketClient(packet);
            targetSlot.MarkDirty();
            sourceSlot.MarkDirty();
        }

        return true;
    }

    internal static void GetSlotsToFlip(in ICoreClientAPI client, KeyCombination t1, bool toOffhand, out ItemSlot? sourceSlot, out ItemSlot? targetSlot)
    {
        IClientPlayer player = client.World.Player;
        IPlayerInventoryManager playerInventory = player.InventoryManager;
        sourceSlot = null;
        targetSlot = null;

        if (lastSwappedSlot.SlotIndex is not null)
        {
            IInventory? inventory = playerInventory.GetOwnInventory(lastSwappedSlot.InvClassName);
            if (inventory is null)
            {
                client.Logger.Error($"Could not find inventory with class name '{lastSwappedSlot.InvClassName}' to swap back light source.");
                return;
            }
            int slotId = lastSwappedSlot.SlotIndex ?? -1;

            ItemSlot? slotToSwapBack = inventory[slotId];
            if (slotToSwapBack is null)
            {
                client.Logger.Error($"Could not find slot with index '{slotId}' in inventory '{lastSwappedSlot.InvClassName}' to swap back light source.");
                return;
            }

            // Swap back the previously swapped out light source
            targetSlot = GetTargetSlot(player, toOffhand);
            sourceSlot = slotToSwapBack;
            return;
        }

        // Find the best light source in hotbar and backpack
        IInventory hotbar = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        IInventory backpack = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);

        CollectibleObject? currentLightSource = null;
        ItemSlot? bestLightSourceSlot = null;
        if (hotbar is not null)
        {
            if (TryFindBetterLightSource(hotbar, currentLightSource, out ItemSlot hotbarSlot))
            {
                currentLightSource = hotbarSlot.Itemstack.Collectible;
                bestLightSourceSlot = hotbarSlot;
            }
        }

        if (backpack is not null)
        {
            if (TryFindBetterLightSource(backpack, currentLightSource, out ItemSlot backpackSlot))
            {
                bestLightSourceSlot = backpackSlot;
            }
        }

        if (bestLightSourceSlot is not null)
        {
            // Equip the best light source found
            targetSlot = GetTargetSlot(player, toOffhand);
            sourceSlot = bestLightSourceSlot;
        }
    }

    internal static bool TryFindBetterLightSource(in IInventory inventory, in CollectibleObject? current, out ItemSlot result)
    {
        result = null!;
        foreach (ItemSlot slot in inventory)
        {
            if (slot.Empty) continue;
            CollectibleObject item = slot.Itemstack.Collectible;
            int itemLightLevel = item.LightHsv[2];
            // check the lightHsv value to know if it's a light source
            if (itemLightLevel <= 0) continue;
            // if we have no current light source, take the first we find
            if (current is null)
            {
                result = slot;
                return true;
            }

            int currentItemLightLevel = current.LightHsv[2];
            // if the found light source is brighter than the current one, take it
            if (itemLightLevel > currentItemLightLevel)
            {
                result = slot;
                return true;
            }
        }
        return false;
    }

    private static ItemSlot GetTargetSlot(in IClientPlayer player, bool useOffhand)
    {
        if (useOffhand)
        {
            return player.Entity.LeftHandItemSlot;
        }
        return player.InventoryManager.ActiveHotbarSlot;
    }
}
