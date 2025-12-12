using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing;

internal enum EStashingState
{
    None,
    PreStashGracePeriod,
    Stashing,
    PostStashGracePeriod
}
/// <summary>
/// Block behavior for containers that can auto-stash items when the interact key is held
/// </summary>
internal class BlockBehaviorAutoStashable : BlockBehavior
{
    #region Constants
    public static string RegistryId => "AutoStashable";
    #endregion

    #region Fields
    protected ICoreAPI? api;
    /// <summary> Time in seconds to wait before stashing items </summary>
    protected float stashDelay = 0.5f;
    /// <summary>
    /// Time in seconds before the auto stash ui appears (to avoid flickering when quickly opening containers)
    /// </summary>
    protected float preStashGracePeriod = 0.1f;
    /// <summary>
    /// Time in seconds after stashing during which the players interaction remains blocked (to avoid the container closing immediately)
    /// </summary>
    protected float postStashGracePeriod = 0.4f;
    protected IProgressBar? progressBar;
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
        if (world.Side == EnumAppSide.Server)
        {
            return true; // Server does not handle interaction
        }

        setProgressVisibility(false);
        BlockEntityContainer blockEntity = world.BlockAccessor.GetBlockEntity<BlockEntityContainer>(blockSel.Position);
        IEnumerable<AssetLocation> stashables = GetStashableItems(byPlayer, blockEntity);
        bool hasStashableItems = stashables.Any();
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
                        bool isActiveHotbarSlotStashable = stashables.Contains(byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Collectible?.Code);
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
        if (secondsUsed > (stashDelay + postStashGracePeriod))
        {
            return false; // Stop interacting
        }

        if (secondsUsed >= preStashGracePeriod)
        {
            setProgressVisibility(true);
            setProgressPercentage(secondsUsed / stashDelay);
            if (stashingState == EStashingState.PreStashGracePeriod)
            {
                stashingState = EStashingState.Stashing;
            }
        }

        if (stashingState >= EStashingState.PostStashGracePeriod)
        {
            return true;// Return here so we don't keep trying to stash after we've already done it.
        }

        if (secondsUsed >= stashDelay)
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
                    return !HasStashables(world, forPlayer)
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
                    return !HasStashables(world, forPlayer)
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
        // TODO: Would like to get a better progress bar, e.g. a more visually appealing circular one.
        ModSystemProgressBar progressBarSystem = api.ModLoader.GetModSystem<ModSystemProgressBar>();
        switch (desiredVisibility)
        {
            case true when progressBar is null:
                {
                    progressBar = progressBarSystem?.AddProgressbar();
                    break;
                }
            case false when progressBar is not null:
                {
                    progressBarSystem?.RemoveProgressbar(progressBar);
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
    /// Gets the item types which are present in both the player's inventory/hotbar AND the specified container.
    /// </summary>
    /// <param name="byPlayer"> The player whose inventory/hotbar to check </param>
    /// <param name="container"> The container whose contents to check </param>
    /// <returns> An enumerable of item types (AssetLocations) which are present in both the player's inventory/hotbar and the container. </returns>
    protected static IEnumerable<AssetLocation> GetStashableItems(in IPlayer byPlayer, in BlockEntityContainer container)
    {
        IPlayerInventoryManager playerInv = byPlayer.InventoryManager;
        IInventory playerBackpack = playerInv.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory playerHotbar = playerInv.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        HashSet<AssetLocation> containerItemTypes = [.. container.GetNonEmptyContentStacks().Select(stack => stack.Collectible.Code)];
        HashSet<AssetLocation> playerItemTypes = [.. GetDistinctItemTypes(playerBackpack), .. GetDistinctItemTypes(playerHotbar)];
        return playerItemTypes.Intersect(containerItemTypes);
    }

    protected static HashSet<AssetLocation> GetDistinctItemTypes(in IInventory inventory)
    {
        return [.. inventory.Where(static slot => !slot.Empty).Select(static slot => slot.Itemstack.Collectible.Code)];
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
        return itemTypesInContainer.Count != 0 && StashMatchingItemsToContainer(world, byPlayer, container, itemTypesInContainer);
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
        return containerAcceptedItem is not null && StashMatchingItemsToContainer(world, byPlayer, container, [containerAcceptedItem]);
    }

    #endregion

    #region Private Implementation

    /// <summary>
    /// Stashes matching items from the player's inventory/hotbar into the specified container.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="container"></param>
    /// <returns></returns>
    protected static bool StashMatchingItemsToContainer(in IWorldAccessor world, in IPlayer byPlayer, in BlockEntityContainer container, in HashSet<AssetLocation> itemAllowList)
    {
        if (itemAllowList.Count == 0)
        {
            return false;
        }
        // Now, go through the player's inventory and stash matching items
        IInventory? backpackInventory = byPlayer.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        IInventory? hotbarInventory = byPlayer.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);

        object openPacket = byPlayer.InventoryManager.OpenInventory(container.Inventory);

        int totalStashed = 0;
        if (backpackInventory is not null)
        {
            totalStashed += AutoStashInventoryIntoContainer(world, byPlayer, itemAllowList, container, backpackInventory);
        }

        if (hotbarInventory is not null)
        {
            totalStashed += AutoStashInventoryIntoContainer(world, byPlayer, itemAllowList, container, hotbarInventory);
        }

        byPlayer.InventoryManager.CloseInventoryAndSync(container.Inventory);
        if (totalStashed > 0)
        {
            container.MarkDirty();
            world.Api?.World.Logger.Audit("'{0}' auto-stashed {1} items into {2} at <{3}>.",
                byPlayer.PlayerName,
                totalStashed,
                container.InventoryClassName,
                container.Pos
            );
        }
        return totalStashed > 0;
    }

    private static int AutoStashInventoryIntoContainer(in IWorldAccessor world, in IPlayer byPlayer, in HashSet<AssetLocation> itemAllowList, in BlockEntityContainer container, in IInventory inventory)
    {
        int totalStashed = 0;

        foreach (ItemSlot? itemSlot in inventory)
        {
            if (!itemSlot.Empty && itemAllowList.Contains(itemSlot.Itemstack.Collectible.Code))
            {
                totalStashed += DumpItemStackIntoContainer(world, byPlayer, container, itemSlot);
            }
        }
        return totalStashed;
    }

    private static int DumpItemStackIntoContainer(in IWorldAccessor world, in IPlayer byPlayer, in BlockEntityContainer container, in ItemSlot itemStack)
    {
        int totalMoved = 0;
        WeightedSlot? targetSlot;
        ItemStackMoveOperation moveOp;
        List<ItemSlot> skipSlots = [];
        do
        {
            moveOp = new(world, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, itemStack.StackSize);
            targetSlot = container.Inventory.GetBestSuitedSlot(itemStack, moveOp, skipSlots);
            if (targetSlot.slot is null)
            {
                break;
            }

            object? packet = byPlayer.InventoryManager.TryTransferTo(itemStack, targetSlot.slot, ref moveOp);
            int acceptedQuantity = moveOp.MovedQuantity;
            totalMoved += acceptedQuantity;

            world.Api?.World.Logger.Audit("'{0}' moved {1}x{2} into {3} at <{4}>.",
                byPlayer.PlayerName,
                targetSlot.slot.Itemstack?.Collectible.Code,
                acceptedQuantity,
                container.InventoryClassName,
                container.Pos
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

    private bool HasStashables(in IWorldAccessor world, in IPlayer player)
    {
        BlockEntityContainer blockEntity = world.BlockAccessor.GetBlockEntity<BlockEntityContainer>(player.CurrentBlockSelection.Position);
        IEnumerable<AssetLocation> stashables = GetStashableItems(player, blockEntity);
        return stashables.Any();
    }
    #endregion
}
