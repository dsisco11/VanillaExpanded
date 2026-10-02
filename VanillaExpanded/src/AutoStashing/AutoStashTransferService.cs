using System.Linq;
using VanillaExpanded.AutoStashing.Transfers;
using VanillaExpanded.AutoStashing.Planning;
using VanillaExpanded.AutoStashing.Targets;

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
        AutoStashTarget target,
        BlockPos targetPos,
        string targetName,
        AutoStashPolicy policy)
    {
        IInventory? backpackInventory = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbarInventory = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        // Read-only candidate detection precedes bag workspace creation; actual capacity is checked after loading.
        if (!target.IsPrepared && (!AutoStashPlanner.HasCandidates(policy, backpackInventory, hotbarInventory) || !target.TryPrepare()))
        {
            return 0;
        }
        IInventory targetInventory = target.Inventory;
        bool canStashBackpack = AutoStashPlanner.HasWork(targetInventory, policy, backpackInventory, null);
        bool canStashHotbar = AutoStashPlanner.HasWork(targetInventory, policy, null, hotbarInventory);
        if (!canStashBackpack && !canStashHotbar)
        {
            return 0;
        }

        target.Acquire(playerInventory);

        int totalStashed = 0;
        bool directMergeFailed = false;
        var mutation = new AutoStashMutationState();
        System.Exception? failure = null;
        try
        {
            using var planner = new AutoStashPlanner(world, targetInventory, policy, backpackInventory, hotbarInventory);
            InventoryTransfer? transfer;
            while ((transfer = planner.GetNextTransfer()) is not null)
            {
                // Preserve uncertainty when an engine callback throws after changing slots.
                mutation.BeginAttempt();
                InventoryTransferResult result = InventoryTransferExecutor.Execute(world, transfer);
                mutation.CompleteAttempt(result.MovedQuantity);
                totalStashed += result.MovedQuantity;
                if (result.MovedQuantity > 0)
                {
                    world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3} at <{4}>.",
                        playerName, result.MovedQuantity, transfer.Destination.Itemstack?.Collectible.Code, targetName, targetPos);
                }
                planner.Advance(transfer, result);
            }
            directMergeFailed = planner.DirectMergeFailed;
        }
        catch (System.Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            mutation.Finish(target, playerInventory, failure, world.Logger);
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

    /// <summary>Adapts existing predicate/preference callers to the policy boundary while preserving lifecycle arguments.</summary>
    internal static int AutoStashToInventory(
        IWorldAccessor world, IPlayerInventoryManager playerInventory, string playerName,
        AutoStashTarget target, BlockPos targetPos, string targetName,
        System.Func<ItemStack, bool> canAccept, System.Func<ItemStack, int?>? getPreferredSlot = null)
    {
        return AutoStashToInventory(world, playerInventory, playerName, target, targetPos, targetName,
            new MatchingContentsPolicy(canAccept, getPreferredSlot));
    }
    #endregion
}
