using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoLoot;

internal static class AutoLootTransferCoordinator
{
    public static void Collect(ICoreClientAPI api, IClientPlayer player, AutoLootTarget target, Func<bool> canContinue)
    {
        InventoryBase inventory = target.Inventory;
        int slotCount = inventory.Count;
        for (int index = 0; index < slotCount && index < inventory.Count; index++)
        {
            if (!canContinue() || !target.IsValid(api, player))
            {
                return;
            }

            ItemSlot source = inventory[index];
            if (source.Empty)
            {
                continue;
            }

            var operation = new ItemStackMoveOperation(api.World, EnumMouseButton.Left,
                EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, source.StackSize)
            {
                ActingPlayer = player
            };
            object[]? packets = player.InventoryManager.TryTransferAway(source, ref operation,
                onlyPlayerInventory: true, slotNotifyEffect: false);
            if (packets is null)
            {
                continue;
            }

            // These packets reconcile work already predicted, even if a transfer callback closed the session.
            foreach (object packet in packets)
            {
                api.Network.SendEntityPacketWithOffset(target.EntityId, 0, packet);
            }
        }
    }
}
