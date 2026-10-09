using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>Reconciles a client-predicted inventory slot to a desired item and quantity using vanilla packets.</summary>
internal static class ClientInventorySlotReconciler
{
    #region Public API

    /// <summary>Removes a mismatched stack or only the excess portion of a matching stack.</summary>
    internal static InventorySlotCorrectionResult RemoveIncorrectOrExcess(
        ICoreClientAPI api,
        IPlayerInventoryManager inventoryManager,
        ItemSlot targetSlot,
        IReadOnlyList<ItemSlot> externalSlots,
        System.Func<ItemStack, bool> matchesDesired,
        int desiredAmount,
        out int retainedAmount,
        System.Func<bool>? isTargetOpen = null,
        System.Func<ItemSlot, bool>? isExternalSlotEligible = null)
    {
        retainedAmount = 0;
        if (isTargetOpen?.Invoke() == false) return InventorySlotCorrectionResult.InventoryClosed;
        if (desiredAmount < 0)
        {
            return InventorySlotCorrectionResult.TransferFailed;
        }

        if (targetSlot.Empty)
        {
            return InventorySlotCorrectionResult.Success;
        }

        bool matchingStack = matchesDesired(targetSlot.Itemstack);
        int removeAmount = matchingStack
            ? Math.Max(0, targetSlot.StackSize - desiredAmount)
            : targetSlot.StackSize;
        retainedAmount = matchingStack ? targetSlot.StackSize - removeAmount : 0;
        if (removeAmount == 0)
        {
            return InventorySlotCorrectionResult.Success;
        }

        int remaining = removeAmount;
        // Stable ordering prefers existing stacks, retaining the policy pool order within each group.
        ItemSlot[] destinations = externalSlots.OrderBy(static slot => slot.Empty).ToArray();
        foreach (ItemSlot destination in destinations)
        {
            if (isTargetOpen?.Invoke() == false) return InventorySlotCorrectionResult.InventoryClosed;
            if (isExternalSlotEligible?.Invoke(destination) == false) continue;
            remaining -= Transfer(api, inventoryManager, targetSlot, destination, remaining,
                () => isTargetOpen?.Invoke() != false && isExternalSlotEligible?.Invoke(destination) != false);
            // Transfer callbacks may close the target; preserve the movement but stop the operation immediately.
            if (isTargetOpen?.Invoke() == false) return InventorySlotCorrectionResult.InventoryClosed;
            if (remaining <= 0) break;
        }

        return remaining == 0
            ? InventorySlotCorrectionResult.Success
            : InventorySlotCorrectionResult.InsufficientSpace;
    }

    /// <summary>Adds only the quantity missing from a target slot.</summary>
    internal static InventorySlotCorrectionResult AddMissing(
        ICoreClientAPI api,
        IPlayerInventoryManager inventoryManager,
        ItemSlot targetSlot,
        IReadOnlyList<ItemSlot> externalSlots,
        System.Func<ItemStack, bool> matchesDesired,
        int amount,
        System.Func<bool>? isTargetOpen = null,
        System.Func<ItemSlot, bool>? isExternalSlotEligible = null)
    {
        if (isTargetOpen?.Invoke() == false) return InventorySlotCorrectionResult.InventoryClosed;
        if (amount < 0) return InventorySlotCorrectionResult.TransferFailed;
        if (amount == 0) return InventorySlotCorrectionResult.Success;
        int remaining = amount;
        foreach (ItemSlot source in externalSlots
            .Where(slot => !slot.Empty && matchesDesired(slot.Itemstack))
            // LINQ sorting is stable, so equal sizes retain backpack, hotbar, then opened-container order.
            .OrderBy(static slot => slot.StackSize).ToArray())
        {
            if (isTargetOpen?.Invoke() == false) return InventorySlotCorrectionResult.InventoryClosed;
            if (isExternalSlotEligible?.Invoke(source) == false) continue;
            if (source.Empty || !matchesDesired(source.Itemstack)) continue;
            remaining -= Transfer(api, inventoryManager, source, targetSlot, remaining,
                () => isTargetOpen?.Invoke() != false && isExternalSlotEligible?.Invoke(source) != false);
            // Transfer callbacks may close the target; preserve the movement but stop the operation immediately.
            if (isTargetOpen?.Invoke() == false) return InventorySlotCorrectionResult.InventoryClosed;
            if (remaining <= 0) break;
        }

        return remaining == 0
            ? InventorySlotCorrectionResult.Success
            : InventorySlotCorrectionResult.InsufficientItems;
    }

    #endregion

    #region Private Methods

    /// <summary>Predicts an exact vanilla slot transfer and sends its synchronization packet.</summary>
    private static int Transfer(
        ICoreClientAPI api,
        IPlayerInventoryManager inventoryManager,
        ItemSlot source,
        ItemSlot target,
        int amount,
        System.Func<bool> isTransferEligible)
    {
        if (amount <= 0 || !AlloyTransferInventoryPolicy.CanReturnTo(target, source)) return 0;

        var operation = new ItemStackMoveOperation(
            api.World,
            EnumMouseButton.Left,
            EnumModifierKey.SHIFT,
            EnumMergePriority.AutoMerge,
            amount)
        {
            ActingPlayer = api.World.Player
        };
        // Specialized permission probes may invoke callbacks too; verify live ownership immediately before moving.
        if (!isTransferEligible()) return 0;
        object? packet = inventoryManager.TryTransferTo(source, target, ref operation);
        if (packet is not null)
        {
            api.Network.SendPacketClient(packet);
        }

        return operation.MovedQuantity;
    }

    #endregion
}