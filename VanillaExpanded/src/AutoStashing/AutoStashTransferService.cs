using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.AutoStashing;

internal static class AutoStashTransferService
{
    #region Transfer Entry Point
    internal static int AutoStashToInventory(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        string playerName,
        IInventory targetInventory,
        BlockPos targetPos,
        string targetName,
        System.Func<ItemStack, bool> canAccept,
        System.Func<ItemStack, int?>? getPreferredSlot = null,
        bool manageInventorySession = true)
    {
        IInventory? backpackInventory = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbarInventory = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        bool canStashBackpack = CanStashAnyItems(backpackInventory, targetInventory, canAccept, getPreferredSlot);
        bool canStashHotbar = CanStashAnyItems(hotbarInventory, targetInventory, canAccept, getPreferredSlot);
        if (!canStashBackpack && !canStashHotbar)
        {
            return 0;
        }

        bool openedForStash = manageInventorySession
            && !(playerInventory.OpenedInventories?.Contains(targetInventory) ?? false);
        if (openedForStash)
        {
            playerInventory.OpenInventory(targetInventory);
        }

        int totalStashed = 0;
        try
        {
            if (backpackInventory is not null)
            {
                totalStashed += TransferSourceInventory(world, playerName, targetInventory, targetPos, targetName, backpackInventory, canAccept, getPreferredSlot);
            }

            if (hotbarInventory is not null)
            {
                totalStashed += TransferSourceInventory(world, playerName, targetInventory, targetPos, targetName, hotbarInventory, canAccept, getPreferredSlot);
            }
        }
        finally
        {
            if (openedForStash)
            {
                playerInventory.CloseInventoryAndSync(targetInventory);
            }
        }

        if (totalStashed > 0)
        {
            world.Api?.World.Logger.Audit("'{0}' auto-stashed {1} items into {2} at <{3}>.",
                playerName, totalStashed, targetName, targetPos);
        }

        return totalStashed;
    }
    #endregion

    #region Slot Transfers
    private static int TransferSourceInventory(
        IWorldAccessor world, string playerName, IInventory targetInventory,
        BlockPos targetPos, string targetName, IInventory sourceInventory,
        System.Func<ItemStack, bool> canAccept, System.Func<ItemStack, int?>? getPreferredSlot)
    {
        int totalStashed = 0;
        foreach (ItemSlot sourceSlot in sourceInventory)
        {
            if (!sourceSlot.Empty && canAccept(sourceSlot.Itemstack))
            {
                totalStashed += TransferItemToInventory(world, playerName, targetInventory, targetPos, targetName, sourceSlot, getPreferredSlot);
            }
        }

        return totalStashed;
    }

    private static int TransferItemToInventory(
        IWorldAccessor world, string playerName, IInventory targetInventory,
        BlockPos targetPos, string targetName, ItemSlot sourceSlot,
        System.Func<ItemStack, int?>? getPreferredSlot)
    {
        int totalMoved = 0;
        List<ItemSlot> skipSlots = [];
        List<ItemSlot> directMergeSlots = [];
        foreach (ItemSlot targetSlot in targetInventory)
        {
            if (!CanAcceptForAutoStash(targetSlot, sourceSlot))
            {
                skipSlots.Add(targetSlot);
            }
        }

        while (!sourceSlot.Empty)
        {
            ItemSlot? targetSlot = null;
            EnumMergePriority mergePriority = EnumMergePriority.AutoMerge;
            if (getPreferredSlot?.Invoke(sourceSlot.Itemstack) is int preferredSlotIndex
                && preferredSlotIndex >= 0 && preferredSlotIndex < targetInventory.Count)
            {
                ItemSlot? candidateSlot = targetInventory[preferredSlotIndex];
                if (candidateSlot is not null && !skipSlots.Contains(candidateSlot) && candidateSlot.CanTakeFrom(sourceSlot))
                {
                    targetSlot = candidateSlot;
                }
            }

            if (targetSlot is null)
            {
                ItemStackMoveOperation findOp = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, sourceSlot.StackSize);
                targetSlot = targetInventory.GetBestSuitedSlot(sourceSlot, findOp, skipSlots)?.slot;
            }

            if (targetSlot is null && directMergeSlots.Count > 0)
            {
                targetSlot = directMergeSlots[0];
                directMergeSlots.RemoveAt(0);
                mergePriority = EnumMergePriority.DirectMerge;
            }

            if (targetSlot is null)
            {
                break;
            }

            int requestedQuantity = sourceSlot.StackSize;
            ItemStackMoveOperation moveOperation = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, mergePriority, requestedQuantity);
            int movedQuantity = sourceSlot.TryPutInto(targetSlot, ref moveOperation);
            totalMoved += movedQuantity;
            if (movedQuantity > 0)
            {
                world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3} at <{4}>.",
                    playerName, movedQuantity, targetSlot.Itemstack?.Collectible.Code, targetName, targetPos);
            }

            skipSlots.Add(targetSlot);
            if (requestedQuantity == movedQuantity)
            {
                break;
            }

            if (movedQuantity == 0 && !targetSlot.Empty
                && moveOperation.RequiredPriority == EnumMergePriority.DirectMerge
                && targetSlot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge))
            {
                directMergeSlots.Add(targetSlot);
            }
        }

        return totalMoved;
    }
    #endregion

    #region Capacity Checks
    private static bool CanStashAnyItems(
        IInventory? sourceInventory, IInventory targetInventory,
        System.Func<ItemStack, bool> canAccept, System.Func<ItemStack, int?>? getPreferredSlot)
    {
        if (sourceInventory is null)
        {
            return false;
        }

        foreach (ItemSlot sourceSlot in sourceInventory)
        {
            if (sourceSlot.Empty || !canAccept(sourceSlot.Itemstack))
            {
                continue;
            }

            if (getPreferredSlot?.Invoke(sourceSlot.Itemstack) is int preferredSlotIndex
                && preferredSlotIndex >= 0 && preferredSlotIndex < targetInventory.Count)
            {
                if (targetInventory[preferredSlotIndex] is ItemSlot preferredSlot
                    && CanAcceptForAutoStash(preferredSlot, sourceSlot))
                {
                    return true;
                }

                continue;
            }

            if (targetInventory.Any(targetSlot => CanAcceptForAutoStash(targetSlot, sourceSlot)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool CanAcceptForAutoStash(ItemSlot targetSlot, ItemSlot sourceSlot)
        => targetSlot.CanTakeFrom(sourceSlot, EnumMergePriority.AutoMerge);
    #endregion
}