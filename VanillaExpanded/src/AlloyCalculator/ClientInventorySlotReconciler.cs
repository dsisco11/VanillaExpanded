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
        IReadOnlyList<ItemSlot> playerSlots,
        System.Func<ItemStack, bool> matchesDesired,
        int desiredAmount,
        out int retainedAmount)
    {
        retainedAmount = 0;
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
        IEnumerable<ItemSlot> destinations = playerSlots
            .Where(slot => !slot.Empty && slot.CanTakeFrom(targetSlot))
            .Concat(playerSlots.Where(slot => slot.Empty && slot.CanTakeFrom(targetSlot)));
        foreach (ItemSlot destination in destinations)
        {
            remaining -= Transfer(api, inventoryManager, targetSlot, destination, remaining);
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
        IReadOnlyList<ItemSlot> playerSlots,
        System.Func<ItemStack, bool> matchesDesired,
        int amount)
    {
        int remaining = amount;
        foreach (ItemSlot source in playerSlots
            .Where(slot => !slot.Empty && matchesDesired(slot.Itemstack))
            .OrderBy(static slot => slot.StackSize))
        {
            remaining -= Transfer(api, inventoryManager, source, targetSlot, remaining);
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
        int amount)
    {
        if (amount <= 0) return 0;

        var operation = new ItemStackMoveOperation(
            api.World,
            EnumMouseButton.Left,
            EnumModifierKey.SHIFT,
            EnumMergePriority.AutoMerge,
            amount)
        {
            ActingPlayer = api.World.Player
        };
        object? packet = inventoryManager.TryTransferTo(source, target, ref operation);
        if (packet is not null)
        {
            api.Network.SendPacketClient(packet);
        }

        return operation.MovedQuantity;
    }

    #endregion
}