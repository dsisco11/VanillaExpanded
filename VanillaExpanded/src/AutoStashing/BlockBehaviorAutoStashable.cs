using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using VanillaExpanded.RadialProgress;

namespace VanillaExpanded.AutoStashing;

/// <summary>
/// Block behavior for containers that can auto-stash items when the interact key is held
/// </summary>
internal class BlockBehaviorAutoStashable : BlockBehavior
{
    #region Constants
    public static string RegistryId => "AutoStashable";
    /// <summary> Time in seconds to wait before stashing items </summary>
    public float StashDelaySeconds = VanillaExpandedModSystem.Config?.AutoStashDelay ?? 0.5f;
    /// <summary>
    /// Time in seconds before the auto stash ui appears (to avoid flickering when quickly opening containers)
    /// </summary>
    public const float PreStashGracePeriodSeconds = 0.1f;
    /// <summary>
    /// Time in seconds after stashing during which the players interaction remains blocked (to avoid the container closing immediately)
    /// </summary>
    public const float PostStashGracePeriodSeconds = 0.4f;
    #endregion

    #region Fields
    protected ICoreAPI? api;
    protected IRadialProgressBar? progressBar;
    protected AssetLocation stashSoundPath = new("game:sounds/player/poultice-applied");
    /// <summary>
    /// Tracks which players are currently stashing items.
    /// </summary>
    //protected HashSet<string> isStashing = [];
    protected EStashingState stashingState = EStashingState.None;
    #endregion

    #region Initialization
    public BlockBehaviorAutoStashable(Block block) : base(block)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        this.api = api;
    }
    #endregion

    #region Interaction Handlers
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
        if (!VanillaExpandedModSystem.Config.EnableAutoStash)
        {
            return true; // Auto-stash is disabled
        }

        if (world.Side == EnumAppSide.Server)
        {
            return true; // Server does not handle interaction
        }

        setProgressVisibility(false);
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        HashSet<int> stashables = GetStashableItems(byPlayer, blockEntity);
        bool hasStashableItems = stashables.Count != 0;
        if (!hasStashableItems)
        {
            return false; // no stashable items, do nothing
        }

        switch (block)
        {
            case BlockCrate:
                {
                    // if the player isnt using the ctrl+shift keys, do not start stashing
                    if (byPlayer.Entity.Controls.CtrlKey && byPlayer.Entity.Controls.ShiftKey)
                    {
                        bool isActiveHotbarSlotStashable = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Id is not null && stashables.Contains(byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack.Collectible.Id);
                        // Check if the active hotbar item is stashable, if so then we do not auto-stash as we want to allow the crate to handle the interaction as it normally would.
                        if (!isActiveHotbarSlotStashable)
                        {
                            handling = EnumHandling.PreventSubsequent;// in the case of crates, this prevents the default storing behavior from occurring.
                            stashingState = EStashingState.PreStashGracePeriod;
                        }
                    }
                    break;
                }
            case BlockGenericTypedContainer:
                {
                    handling = EnumHandling.PreventDefault;
                    stashingState = EStashingState.PreStashGracePeriod;
                    break;
                }
            case BlockBloomery:
                {
                    handling = EnumHandling.PreventDefault;
                    stashingState = EStashingState.PreStashGracePeriod;
                    break;
                }
            default:
                world.Logger.Error($"[{nameof(BlockBehaviorAutoStashable)}][{nameof(OnBlockInteractStart)}] unsupported block type: {block.Class} ({block.Code})");
                break;
        }

        return true;
    }

    public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        if (world.Side != EnumAppSide.Client)
        {
            return true;
        }

        if (stashingState == EStashingState.None)
        {
            return true; // Not stashing, do nothing.
        }

        handling = EnumHandling.PreventSubsequent;
        // Allow a grace period after stashing to avoid immediate re-closure of the container.
        if (secondsUsed > (StashDelaySeconds + PostStashGracePeriodSeconds))
        {
            return false; // Stop interacting
        }

        if (secondsUsed >= PreStashGracePeriodSeconds)
        {
            setProgressVisibility(true);
            setProgressPercentage(secondsUsed / StashDelaySeconds);
            if (stashingState == EStashingState.PreStashGracePeriod)
            {
                stashingState = EStashingState.Stashing;
            }
        }

        if (stashingState >= EStashingState.PostStashGracePeriod)
        {
            return true;// Return here so we don't keep trying to stash after we've already done it.
        }

        if (secondsUsed >= StashDelaySeconds)
        {
            stashingState = EStashingState.PostStashGracePeriod;
            world.Api.ModLoader.GetModSystem<AutoStashSystem_Client>().RequestAutoStash(blockSel.Position);
            setProgressVisibility(false);
            handleDidMoveItems(byPlayer);
        }

        return true;
    }

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        stashingState = EStashingState.None;
        setProgressVisibility(false);
        return base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        stashingState = EStashingState.None;
        setProgressVisibility(false);
        handling = EnumHandling.Handled;
    }
    #endregion

    #region Private
    /// <summary>
    /// Attempts to stash items from the player's inventory into the specified container at the given block selection.
    /// </summary>
    public void TryStashPlayerInventory(in IWorldAccessor world, in IPlayer byPlayer, in BlockPos position)
    {
        if (world.Side == EnumAppSide.Client)
        {
            return;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(position);
        if (be is BlockEntityCrate crateEntity)
        {
            AutoStashToCrate(world, byPlayer, crateEntity);
        }
        else if (be is BlockEntityBloomery bloomeryEntity)
        {
            AutoStashToBloomery(world, byPlayer, bloomeryEntity);
        }
        else if (be is BlockEntityContainer containerEntity)
        {
            AutoStashToGenericContainer(world, byPlayer, containerEntity);
        }
    }

    /// <summary>
    /// Handles the event when items have been moved into the container.
    /// </summary>
    private void handleDidMoveItems(in IPlayer byPlayer)
    {
        IWorldAccessor world = byPlayer.Entity.World;
        //if (world.Side == EnumAppSide.Server)
        {
            world.PlaySoundAt(stashSoundPath, byPlayer.Entity, null, false, 16, volume: 1.0f);
        }

        if (api is ICoreClientAPI client)
        {
            client.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        }
    }
    #endregion

    #region World Interaction Help
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
    {
        switch (block.EntityClass)
        {
            case "Crate":
                {
                    // If the player has no stashable items, do not show the interaction help.
                    return !HasStashables(world, forPlayer, selection)
                        ? []
                        : [
                        new WorldInteraction()
                        {
                            ActionLangCode = "vanillaexpanded:blockhelp-autostash-container",
                            MouseButton = EnumMouseButton.Right,
                            HotKeyCodes = ["ctrl", "shift"],
                        }
                    ];
                }
            default:
                {
                    // If the player has no stashable items, do not show the interaction help.
                    return !HasStashables(world, forPlayer, selection)
                        ? []
                        : [
                        new WorldInteraction()
                        {
                            ActionLangCode = "vanillaexpanded:blockhelp-autostash-container",
                            MouseButton = EnumMouseButton.Right,
                        }
                    ];
                }
        }
    }
    #endregion

    #region UI Management
    private void setProgressVisibility(bool desiredVisibility)
    {
        if (api?.Side != EnumAppSide.Client)
        {
            return;
        }
        ModSystemRadialProgressBar? progressBarSystem = api.ModLoader.GetModSystem<ModSystemRadialProgressBar>();
        switch (desiredVisibility)
        {
            case true when progressBar is null:
                {
                    progressBar = progressBarSystem?.AddProgressBar();
                    break;
                }
            case false when progressBar is not null:
                {
                    progressBarSystem?.RemoveProgressBar(progressBar);
                    progressBar = null;
                    break;
                }
        }
    }

    private void setProgressPercentage(float progress)
    {
        if (api?.Side != EnumAppSide.Client || progressBar is null)
        {
            return;
        }
        progressBar.Progress = Math.Clamp(progress, 0f, 1f);
    }
    #endregion

    #region AutoStashing Implementation

    /// <summary>
    /// Gets the item types which are present in both the player's inventory/hotbar AND can be stashed into the specified block entity.
    /// Handles both containers and bloomeries, including state validation (e.g., bloomery not burning, output empty).
    /// </summary>
    /// <param name="byPlayer"> The player whose inventory/hotbar to check </param>
    /// <param name="blockEntity"> The block entity to check (container or bloomery) </param>
    /// <returns> A set of collectible IDs which can be stashed from the player into the block entity. </returns>
    internal static HashSet<int> GetStashableItems(in IPlayer byPlayer, in BlockEntity? blockEntity)
    {
        return blockEntity switch
        {
            BlockEntityBloomery bloomery => GetStashableItemsForBloomery(byPlayer, bloomery),
            BlockEntityContainer container => GetStashableItemsForContainer(byPlayer, container),
            _ => []
        };
    }

    /// <summary>
    /// Gets item types which are present in both the player's inventory/hotbar AND the specified container.
    /// </summary>
    private static HashSet<int> GetStashableItemsForContainer(in IPlayer byPlayer, in BlockEntityContainer container)
    {
        if (container is null)
        {
            return [];
        }

        IPlayerInventoryManager playerInv = byPlayer.InventoryManager;
        IInventory playerBackpack = playerInv.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory playerHotbar = playerInv.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        HashSet<int> containerItemTypes = [.. container.GetNonEmptyContentStacks().Where(static stack => stack?.Collectible?.Id is not null).Select(static stack => stack.Collectible.Id)];
        HashSet<int> playerItemTypes = [.. GetDistinctItemTypes(playerBackpack), .. GetDistinctItemTypes(playerHotbar)];
        containerItemTypes.IntersectWith(playerItemTypes);
        return containerItemTypes;
    }

    /// <summary>
    /// Gets item types which can be stashed into a bloomery.
    /// Returns empty set if bloomery is burning or has items in output slot.
    /// </summary>
    private static HashSet<int> GetStashableItemsForBloomery(in IPlayer byPlayer, in BlockEntityBloomery bloomery)
    {
        if (bloomery is null)
        {
            return [];
        }

        // Bloomery state validation: cannot add items while burning or if output slot has items
        InventoryGeneric? bloomeryInv = BloomeryAccessor.GetInventory(bloomery);
        if (bloomery.IsBurning || bloomeryInv is null || !bloomeryInv[2].Empty)
        {
            return [];
        }

        IPlayerInventoryManager playerInv = byPlayer.InventoryManager;
        IInventory playerBackpack = playerInv.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory playerHotbar = playerInv.GetOwnInventory(GlobalConstants.hotBarInvClassName);

        // Find player items that the bloomery can accept
        HashSet<int> stashableIds = [];
        foreach (ItemSlot slot in playerBackpack.Concat(playerHotbar))
        {
            if (slot.Empty || slot.Itemstack?.Collectible?.Id is null)
            {
                continue;
            }

            if (bloomery.CanAdd(slot.Itemstack))
            {
                stashableIds.Add(slot.Itemstack.Collectible.Id);
            }
        }

        return stashableIds;
    }

    internal static HashSet<int> GetDistinctItemTypes(in IInventory inventory)
    {
        return [.. inventory.Where(static slot => !slot.Empty).Where(static slot => slot?.Itemstack?.Collectible?.Id is not null).Select(static slot => slot.Itemstack.Collectible.Id)];
    }

    /// <summary>
    /// Automatically stashes items from the player's inventory into the specified generic container.
    /// The item-types which are already present in the container are the ones which will be stashed.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="container"></param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    public static bool AutoStashToGenericContainer(in IWorldAccessor world, in IPlayer byPlayer, in BlockEntityContainer container)
    {
        HashSet<AssetLocation> itemTypesInContainer = [.. container.GetNonEmptyContentStacks().Select(static stack => stack.Collectible.Code)];
        return itemTypesInContainer.Count != 0 && AutoStashToInventory(
            world,
            byPlayer,
            container.Inventory,
            container.Pos,
            container.InventoryClassName,
            stack => itemTypesInContainer.Contains(stack.Collectible.Code));
    }

    /// <summary>
    /// Automatically stashes items from the player's inventory into the specified crate container.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="container"></param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    public static bool AutoStashToCrate(in IWorldAccessor world, in IPlayer byPlayer, in BlockEntityCrate container)
    {
        AssetLocation? containerAcceptedItem = container.Inventory.FirstNonEmptySlot?.Itemstack?.Collectible?.Code;
        return containerAcceptedItem is not null && AutoStashToInventory(
            world,
            byPlayer,
            container.Inventory,
            container.Pos,
            container.InventoryClassName,
            stack => stack.Collectible.Code.Equals(containerAcceptedItem));
    }

    /// <summary>
    /// Automatically stashes items from the player's inventory into the specified bloomery.
    /// Stashes valid fuel to slot 0 and valid ore to slot 1.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="bloomery"></param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    public static bool AutoStashToBloomery(IWorldAccessor world, IPlayer byPlayer, BlockEntityBloomery bloomery)
    {
        InventoryGeneric? bloomeryInv = BloomeryAccessor.GetInventory(bloomery);
        if (bloomeryInv is null || bloomery.IsBurning || !bloomeryInv[2].Empty)
        {
            return false;
        }

        return AutoStashToInventory(
            world,
            byPlayer,
            bloomeryInv,
            bloomery.Pos,
            "bloomery",
            stack => bloomery.CanAdd(stack),
            GetBloomeryPreferredSlot);
    }

    /// <summary>
    /// Determines the preferred slot index for an item in a bloomery.
    /// Returns slot 0 for fuel, slot 1 for ore, or null if item is not valid.
    /// </summary>
    private static int? GetBloomeryPreferredSlot(ItemStack stack)
    {
        if (stack?.Collectible?.CombustibleProps is not CombustibleProperties combustProps)
        {
            return null;
        }

        // Ore: has SmeltedStack and melting point in range
        if (combustProps.SmeltedStack is not null
            && combustProps.MeltingPoint >= BlockEntityBloomery.MinTemp
            && combustProps.MeltingPoint < BlockEntityBloomery.MaxTemp)
        {
            return 1; // Ore slot
        }

        // Fuel: high burn temperature and duration
        if (combustProps.BurnTemperature >= 1200 && combustProps.BurnDuration > 30)
        {
            return 0; // Fuel slot
        }

        return null;
    }

    #endregion

    #region Private Implementation

    /// <summary>
    /// Unified method to stash items from player's inventory into any inventory.
    /// Uses a predicate to determine which items can be stashed, and optionally a slot selector for targeted stashing.
    /// </summary>
    /// <param name="world">The world accessor.</param>
    /// <param name="byPlayer">The player whose inventory to stash from.</param>
    /// <param name="targetInventory">The inventory to stash items into.</param>
    /// <param name="targetPos">The position of the target block entity (for logging).</param>
    /// <param name="targetName">The name of the target (for logging).</param>
    /// <param name="canAccept">Predicate that returns true if the item can be stashed.</param>
    /// <param name="getPreferredSlot">Optional function to get the preferred slot index for an item. If null, uses GetBestSuitedSlot.</param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    protected static bool AutoStashToInventory(
        in IWorldAccessor world,
        in IPlayer byPlayer,
        in IInventory targetInventory,
        in BlockPos targetPos,
        in string targetName,
        System.Func<ItemStack, bool> canAccept,
        System.Func<ItemStack, int?>? getPreferredSlot = null)
    {
        IInventory? backpackInventory = byPlayer.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbarInventory = byPlayer.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);

        _ = byPlayer.InventoryManager.OpenInventory(targetInventory);

        int totalStashed = 0;
        if (backpackInventory is not null)
        {
            totalStashed += AutoStashInventoryIntoInventory(world, byPlayer, targetInventory, targetPos, targetName, backpackInventory, canAccept, getPreferredSlot);
        }

        if (hotbarInventory is not null)
        {
            totalStashed += AutoStashInventoryIntoInventory(world, byPlayer, targetInventory, targetPos, targetName, hotbarInventory, canAccept, getPreferredSlot);
        }

        byPlayer.InventoryManager.CloseInventoryAndSync(targetInventory);
        if (totalStashed > 0)
        {
            world.Api?.World.Logger.Audit("'{0}' auto-stashed {1} items into {2} at <{3}>.",
                byPlayer.PlayerName,
                totalStashed,
                targetName,
                targetPos
            );
        }
        return totalStashed > 0;
    }

    private static int AutoStashInventoryIntoInventory(
        in IWorldAccessor world,
        in IPlayer byPlayer,
        in IInventory targetInventory,
        in BlockPos targetPos,
        in string targetName,
        in IInventory sourceInventory,
        System.Func<ItemStack, bool> canAccept,
        System.Func<ItemStack, int?>? getPreferredSlot)
    {
        int totalStashed = 0;

        foreach (ItemSlot? itemSlot in sourceInventory)
        {
            if (itemSlot.Empty || !canAccept(itemSlot.Itemstack))
            {
                continue;
            }

            totalStashed += TransferItemToInventory(world, byPlayer, targetInventory, targetPos, targetName, itemSlot, getPreferredSlot);
        }
        return totalStashed;
    }

    private static int TransferItemToInventory(
        in IWorldAccessor world,
        in IPlayer byPlayer,
        in IInventory targetInventory,
        in BlockPos targetPos,
        in string targetName,
        in ItemSlot sourceSlot,
        System.Func<ItemStack, int?>? getPreferredSlot)
    {
        int totalMoved = 0;
        List<ItemSlot> skipSlots = [];

        while (!sourceSlot.Empty)
        {
            ItemSlot? targetSlot = null;

            // If a preferred slot function is provided, try to use it
            if (getPreferredSlot is not null)
            {
                int? preferredSlotIndex = getPreferredSlot(sourceSlot.Itemstack);
                if (preferredSlotIndex.HasValue && preferredSlotIndex.Value < targetInventory.Count)
                {
                    ItemSlot candidateSlot = targetInventory[preferredSlotIndex.Value];
                    if (!skipSlots.Contains(candidateSlot) && candidateSlot.CanTakeFrom(sourceSlot))
                    {
                        targetSlot = candidateSlot;
                    }
                }
            }

            // Fall back to GetBestSuitedSlot if no preferred slot or preferred slot can't accept
            if (targetSlot is null)
            {
                ItemStackMoveOperation findOp = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, sourceSlot.StackSize);
                WeightedSlot? bestSlot = targetInventory.GetBestSuitedSlot(sourceSlot, findOp, skipSlots);
                targetSlot = bestSlot.slot;
            }

            if (targetSlot is null)
            {
                break;
            }

            ItemStackMoveOperation moveOp = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, sourceSlot.StackSize);
            object? packet = byPlayer.InventoryManager.TryTransferTo(sourceSlot, targetSlot, ref moveOp);
            int movedQuantity = moveOp.MovedQuantity;
            totalMoved += movedQuantity;

            if (movedQuantity > 0)
            {
                world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3} at <{4}>.",
                    byPlayer.PlayerName,
                    movedQuantity,
                    targetSlot.Itemstack?.Collectible.Code,
                    targetName,
                    targetPos
                );
            }

            skipSlots.Add(targetSlot);

            if (packet is not null)
            {
                targetSlot.MarkDirty();
                sourceSlot.MarkDirty();
            }

            if (moveOp.NotMovedQuantity == 0 || movedQuantity == 0)
            {
                break;
            }
        }
        return totalMoved;
    }

    private bool HasStashables(in IWorldAccessor world, in IPlayer player, BlockSelection? selection = null)
    {
        selection ??= player.CurrentBlockSelection;
        if (selection is null)
        {
            return false;
        }

        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
        HashSet<int> stashables = GetStashableItems(player, blockEntity);
        return stashables.Count != 0;
    }
    #endregion
}
