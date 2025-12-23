using System.Collections.Generic;
using System.ComponentModel;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded;
internal static class IPlayerExtensions
{
    /// <summary>
    /// Deposits as much of the given item stacks into the destination inventory as possible, using shift-click logic.
    /// </summary>
    /// <param name="destinationInventory"> The inventory to deposit into. </param>
    /// <param name="itemStack"> The item stack to deposit. </param>
    /// <returns> The total number of items moved into the destination inventory. </returns>
    public static int DepositItemStacks(this IPlayer byPlayer, in IWorldAccessor world, in IInventory destinationInventory, IEnumerable<ItemSlot> itemStacks)
    {
        bool isDestinationOpen = byPlayer.InventoryManager.OpenedInventories.Contains(destinationInventory);
        if (!isDestinationOpen)
        {
            var openPkt = byPlayer.InventoryManager.OpenInventory(destinationInventory);
            if (openPkt is null)
            {
                world.Logger.Warning("Failed to open inventory {0} for player {1} when attempting to deposit items.", destinationInventory.ClassName, byPlayer.PlayerName);
                return 0;
            }
        }

        int totalMoved = 0;
        foreach (ItemSlot itemStack in itemStacks)
        {
            totalMoved += _deposit(byPlayer, world, itemStack, destinationInventory);
        }

        byPlayer.InventoryManager.CloseInventoryAndSync(destinationInventory);
        if (totalMoved > 0)
        {
            world.Api?.World.Logger.Audit("'{0}' transfered {1} items into {2}.",
                byPlayer.PlayerName,
                totalMoved,
                destinationInventory.ClassName
            );
        }
        return totalMoved;
    }

    /// <summary>
    /// Deposits as much of the given item stack into the destination inventory as possible, using shift-click logic.
    /// </summary>
    /// <param name="destinationInventory"> The inventory to deposit into. </param>
    /// <param name="itemStack"> The item stack to deposit. </param>
    /// <returns> The total number of items moved into the destination inventory. </returns>
    private static int _deposit(in IPlayer byPlayer, in IWorldAccessor world, in ItemSlot itemStack, in IInventory destinationInventory)
    {
        IPlayerInventoryManager invManager = byPlayer.InventoryManager;
        int totalMoved = 0;
        WeightedSlot? targetSlot;
        ItemStackMoveOperation moveOp;
        List<ItemSlot> skipSlots = [];
        do
        {
            moveOp = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, itemStack.StackSize);
            targetSlot = destinationInventory.GetBestSuitedSlot(itemStack, moveOp, skipSlots);
            if (targetSlot.slot is null)
            {
                break;
            }

            object? packet = byPlayer.InventoryManager.TryTransferTo(itemStack, targetSlot.slot, ref moveOp);
            int acceptedQuantity = moveOp.MovedQuantity;
            totalMoved += acceptedQuantity;

            world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3}.",
                byPlayer.PlayerName,
                targetSlot.slot.Itemstack?.Collectible.Code,
                acceptedQuantity,
                destinationInventory.ClassName
            );

            skipSlots.Add(targetSlot.slot);

            if (packet is not null)
            {
                targetSlot.slot.MarkDirty();
                itemStack.MarkDirty();
            }
        }
        while (targetSlot is not null && moveOp.NotMovedQuantity > 0);
        return totalMoved;
    }
}
