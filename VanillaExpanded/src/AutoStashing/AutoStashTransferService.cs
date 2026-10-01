using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VanillaExpanded.AutoStashing;

/// <summary>Executes engine-owned inventory moves with bounded destination selection and session ownership.</summary>
internal static class AutoStashTransferService
{
    #region Public API
    /// <summary>Transfers accepted items, finalizes applied or uncertain changes before session cleanup, and reports exhausted direct merges.</summary>
    internal static int AutoStashToInventory(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        string playerName,
        IInventory targetInventory,
        BlockPos targetPos,
        string targetName,
        System.Func<ItemStack, bool> canAccept,
        System.Func<ItemStack, int?>? getPreferredSlot = null,
        bool manageInventorySession = true,
        System.Action? finalizeChanges = null)
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
        bool directMergeFailed = false;
        var mutation = new AutoStashMutationState();
        System.Exception? failure = null;
        try
        {
            if (backpackInventory is not null)
            {
                totalStashed += TransferSourceInventory(world, playerName, targetInventory, targetPos, targetName, backpackInventory, canAccept, getPreferredSlot, ref directMergeFailed, mutation);
            }

            if (hotbarInventory is not null)
            {
                totalStashed += TransferSourceInventory(world, playerName, targetInventory, targetPos, targetName, hotbarInventory, canAccept, getPreferredSlot, ref directMergeFailed, mutation);
            }
        }
        catch (System.Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            mutation.Finish(finalizeChanges,
                openedForStash ? () => playerInventory.CloseInventoryAndSync(targetInventory) : null,
                failure, world.Logger);
        }

        if (directMergeFailed)
        {
            // Use inventory ownership rather than a display name to address the originating player.
            if (world.AllOnlinePlayers?.FirstOrDefault(player => ReferenceEquals(player.InventoryManager, playerInventory)) is IServerPlayer player)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup,
                    "AutoStash could not move remaining items after trying every compatible destination.",
                    EnumChatType.CommandError);
            }
        }

        if (totalStashed > 0)
        {
            world.Api?.World.Logger.Audit("'{0}' auto-stashed {1} items into {2} at <{3}>.",
                playerName, totalStashed, targetName, targetPos);
        }

        return totalStashed;
    }

    /// <summary>Checks engine eligibility for automatic destination selection.</summary>
    internal static bool CanAcceptForAutoStash(ItemSlot targetSlot, ItemSlot sourceSlot)
        => targetSlot.CanTakeFrom(sourceSlot, EnumMergePriority.AutoMerge);
    #endregion

    #region Private
    #region Slot Transfers
    /// <summary>Visits source slots in inventory order and aggregates direct-merge failure feedback.</summary>
    private static int TransferSourceInventory(
        IWorldAccessor world, string playerName, IInventory targetInventory,
        BlockPos targetPos, string targetName, IInventory sourceInventory,
        System.Func<ItemStack, bool> canAccept, System.Func<ItemStack, int?>? getPreferredSlot, ref bool directMergeFailed, AutoStashMutationState mutation)
    {
        int totalStashed = 0;
        foreach (ItemSlot sourceSlot in sourceInventory)
        {
            if (!sourceSlot.Empty && canAccept(sourceSlot.Itemstack))
            {
                totalStashed += TransferItemToInventory(world, playerName, targetInventory, targetPos, targetName, sourceSlot, getPreferredSlot, ref directMergeFailed, mutation);
            }
        }

        return totalStashed;
    }

    /// <summary>Exhausts automatic selection, then tries every eligible direct destination until progress stops.</summary>
    private static int TransferItemToInventory(
        IWorldAccessor world, string playerName, IInventory targetInventory,
        BlockPos targetPos, string targetName, ItemSlot sourceSlot,
        System.Func<ItemStack, int?>? getPreferredSlot, ref bool directMergeFailed, AutoStashMutationState mutation)
    {
        int totalMoved = 0;
        List<ItemSlot> skipSlots = [];
        List<ItemSlot> deferredDirectSlots = [];
        HashSet<ItemSlot> rejectedDirectSlots = [];
        bool directPhase = false;
        bool rejectedDirectMove = false;
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
            if (!directPhase && getPreferredSlot?.Invoke(sourceSlot.Itemstack) is int preferredSlotIndex
                && preferredSlotIndex >= 0 && preferredSlotIndex < targetInventory.Count)
            {
                ItemSlot? candidateSlot = targetInventory[preferredSlotIndex];
                if (candidateSlot is not null && !skipSlots.Contains(candidateSlot) && candidateSlot.CanTakeFrom(sourceSlot))
                {
                    targetSlot = candidateSlot;
                }
            }

            if (targetSlot is null && !directPhase)
            {
                ItemStackMoveOperation findOp = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, sourceSlot.StackSize);
                targetSlot = targetInventory.GetBestSuitedSlot(sourceSlot, findOp, skipSlots)?.slot;
            }

            if (targetSlot is null)
            {
                // Scan all live slots, including empty slots and slots excluded by automatic eligibility.
                // A zero-move direct attempt excludes that slot until another move makes progress.
                directPhase = true;
                targetSlot = deferredDirectSlots.FirstOrDefault(slot => !rejectedDirectSlots.Contains(slot)
                    && slot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge));
                if (targetSlot is not null) deferredDirectSlots.Remove(targetSlot);
                targetSlot ??= targetInventory.FirstOrDefault(slot => !rejectedDirectSlots.Contains(slot)
                    && slot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge));
                mergePriority = EnumMergePriority.DirectMerge;
            }

            if (targetSlot is null)
            {
                directMergeFailed |= rejectedDirectMove;
                break;
            }

            int requestedQuantity = sourceSlot.StackSize;
            ItemStackMoveOperation moveOperation = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, mergePriority, requestedQuantity);
            // Engine callbacks may throw after mutation, before a moved count can be returned.
            mutation.BeginAttempt();
            int movedQuantity = sourceSlot.TryPutInto(targetSlot, ref moveOperation);
            mutation.CompleteAttempt(movedQuantity);
            totalMoved += movedQuantity;
            if (movedQuantity > 0)
            {
                world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3} at <{4}>.",
                    playerName, movedQuantity, targetSlot.Itemstack?.Collectible.Code, targetName, targetPos);
            }

            skipSlots.Add(targetSlot);
            if (mergePriority == EnumMergePriority.DirectMerge && movedQuantity > 0)
            {
                // A smaller remainder or changed merge state can make a previously rejected slot usable.
                // Recheck those slots after progress; each reset consumes source items, keeping retries bounded.
                rejectedDirectSlots.Clear();
                rejectedDirectMove = false;
            }
            if (requestedQuantity == movedQuantity)
            {
                break;
            }

            if (mergePriority == EnumMergePriority.DirectMerge && movedQuantity == 0)
            {
                rejectedDirectSlots.Add(targetSlot);
                rejectedDirectMove = true;
            }
            else if (mergePriority == EnumMergePriority.AutoMerge && movedQuantity == 0
                && moveOperation.RequiredPriority == EnumMergePriority.DirectMerge)
            {
                deferredDirectSlots.Add(targetSlot);
            }
            else if (mergePriority == EnumMergePriority.DirectMerge && movedQuantity > 0
                && targetSlot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge))
            {
                // Progress reduces the finite source quantity, so finishing this destination cannot loop forever.
                deferredDirectSlots.Insert(0, targetSlot);
            }
        }

        return totalMoved;
    }
    #endregion

    #region Capacity Checks
    /// <summary>Checks current capacity while preserving preferred-slot preflight exclusivity.</summary>
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
                    && (CanAcceptForAutoStash(preferredSlot, sourceSlot)
                        || preferredSlot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge)))
                {
                    return true;
                }

                continue;
            }

            if (targetInventory.Any(targetSlot => CanAcceptForAutoStash(targetSlot, sourceSlot)
                || targetSlot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge)))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
    #endregion
}
