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
internal static class AutoStashService
{
    #region Public API
    #region Advisory assessment
    /// <summary>Resolves a read-only block view and policy while treating interaction eligibility as a separate caller input.</summary>
    internal static AutoStashAssessment AssessBlock(IPlayerInventoryManager owner, BlockEntity? blockEntity,
        bool interactionAllowed)
    {
        if (!interactionAllowed) return AutoStashAssessment.Empty;
        AutoStashPolicy policy;
        IInventory inventory;
        if (blockEntity is Vintagestory.GameContent.BlockEntityBloomery bloomery)
        {
            var target = BloomeryAutoStashTarget.Resolve(bloomery);
            if (target is null) return AutoStashAssessment.Empty;
            inventory = target.Inventory;
            policy = new BloomeryPolicy(bloomery, (InventoryGeneric)inventory);
        }
        else if (blockEntity is Vintagestory.GameContent.BlockEntityContainer container)
        {
            inventory = container.Inventory;
            // Existing crate help considers all current contents, unlike first-type crate execution.
            // Retain client ID matching instead of silently broadening it to execution's code matching.
            policy = MatchingContentsPolicy.ForAssessment(container.GetNonEmptyContentStacks());
        }
        else return AutoStashAssessment.Empty;

        return AutoStashAssessor.Assess(policy, inventory,
            owner.GetOwnInventory(GlobalConstants.backpackInvClassName), owner.GetOwnInventory(GlobalConstants.hotBarInvClassName));
    }

    /// <summary>Reads persisted bag contents only; candidate detection does not claim available space or load execution slots.</summary>
    internal static AutoStashAssessment AssessAttached(IWorldAccessor world, IPlayerInventoryManager owner, ItemSlot attachment)
    {
        ItemStack? stack = attachment.Itemstack;
        IHeldBag? bag = stack?.Collectible.GetCollectibleInterface<IHeldBag>();
        if (stack is null || bag is null) return AutoStashAssessment.Empty;
        // Stock bags without persisted backpack data return null and have no matching contents.
        var policy = MatchingContentsPolicy.ForAssessment(bag.GetContents(stack, world) ?? []);
        return AutoStashAssessor.Assess(policy, null,
            owner.GetOwnInventory(GlobalConstants.backpackInvClassName), owner.GetOwnInventory(GlobalConstants.hotBarInvClassName));
    }
    #endregion

    #region Target operations
    /// <summary>Captures container matching types before acquiring its execution session.</summary>
    internal static AutoStashResult StashContainer(IWorldAccessor world, IPlayerInventoryManager owner,
        Vintagestory.GameContent.BlockEntityContainer container, string playerName, bool crate)
    {
        var target = new ContainerAutoStashTarget(container);
        AutoStashPolicy policy = crate ? new CratePolicy(container.Inventory) : new MatchingContentsPolicy(target.GetContents());
        return Execute(world, owner, playerName, target, container.Pos, container.InventoryClassName, policy);
    }

    /// <summary>Resolves current bloomery availability and retains mandatory live input routing.</summary>
    internal static AutoStashResult StashBloomery(IWorldAccessor world, IPlayerInventoryManager owner,
        Vintagestory.GameContent.BlockEntityBloomery bloomery, string playerName)
    {
        var target = BloomeryAutoStashTarget.Resolve(bloomery);
        if (target is null) return new(0, AutoStashOutcome.Unavailable);
        return Execute(world, owner, playerName, target, bloomery.Pos, "bloomery",
            new BloomeryPolicy(bloomery, (InventoryGeneric)target.Inventory), new AutoStashAuditContext(bloomery.Pos, "bloomery", true));
    }

    /// <summary>Snapshots persisted bag types before preparing either workspace-backed or temporary execution slots.</summary>
    internal static AutoStashResult StashAttached(IWorldAccessor world, IPlayer player,
        Vintagestory.API.Common.Entities.Entity host, Vintagestory.GameContent.EntityBehaviorAttachable attachable, int index)
    {
        var target = AttachedBagAutoStashTarget.Resolve(world, host, attachable, index);
        if (target is null) return new(0, AutoStashOutcome.Unavailable);
        return Execute(world, player.InventoryManager, player.PlayerName, target, host.Pos.AsBlockPos,
            $"attached container on {host.Code}", new MatchingContentsPolicy(target.GetContents()));
    }
    #endregion

    #region Operation coordination
    /// <summary>Transfers accepted items, finalizes applied or uncertain changes before session cleanup, and reports exhausted direct merges.</summary>
    internal static AutoStashResult Execute(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        string playerName,
        AutoStashTarget? target,
        BlockPos targetPos,
        string targetName,
        AutoStashPolicy policy, AutoStashAuditContext? audit = null)
    {
        if (target is null) return new(0, AutoStashOutcome.Unavailable);
        audit ??= new AutoStashAuditContext(targetPos, targetName);
        IInventory? backpackInventory = playerInventory.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbarInventory = playerInventory.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        // Read-only candidate detection precedes bag workspace creation; actual capacity is checked after loading.
        if (!AutoStashPlanner.HasCandidates(policy, backpackInventory, hotbarInventory)) return new(0, AutoStashOutcome.NoCandidates);
        if (!target.IsPrepared && !target.TryPrepare())
        {
            return new(0, AutoStashOutcome.Unavailable);
        }
        IInventory targetInventory = target.Inventory;
        bool canStashBackpack = AutoStashPlanner.HasWork(targetInventory, policy, backpackInventory, null);
        bool canStashHotbar = AutoStashPlanner.HasWork(targetInventory, policy, null, hotbarInventory);
        if (!canStashBackpack && !canStashHotbar)
        {
            return new(0, AutoStashOutcome.NoDestination);
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
                    audit.Moved(world, playerName, result.MovedQuantity, transfer.Destination.Itemstack?.Collectible.Code);
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
            audit.Completed(world, playerName, totalStashed);
        }

        return new(totalStashed, totalStashed > 0 ? AutoStashOutcome.Success : AutoStashOutcome.NoDestination, directMergeFailed);
    }

    #endregion
    #endregion
}
