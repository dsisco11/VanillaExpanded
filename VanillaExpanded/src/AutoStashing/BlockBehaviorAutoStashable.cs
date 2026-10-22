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
using VanillaExpanded.AutoStashing.Planning;

namespace VanillaExpanded.AutoStashing;

/// <summary>
/// Block behavior for containers that can auto-stash items when the interact key is held
/// </summary>
internal class BlockBehaviorAutoStashable : BlockBehavior
{
    #region Constants
    public static string RegistryId => "AutoStashable";
    /// <summary> Time in seconds to wait before stashing items </summary>
    public float StashDelaySeconds => VanillaExpandedModSystem.Config?.AutoStashDelay ?? 0.5f;
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
    protected IRadialProgressBar? progressBar => hold?.ProgressBar;
    protected AssetLocation stashSoundPath = new("game:sounds/player/poultice-applied");
    /// <summary>
    /// Tracks which players are currently stashing items.
    /// </summary>
    //protected HashSet<string> isStashing = [];
    protected EStashingState stashingState = EStashingState.None;
    private IProgressSystemProvider? progressSystem;
    private HoldInteractionProgress? hold;
    #endregion

    #region Initialization
    /// <summary>Creates the engine-registered behavior using the existing progress manager.</summary>
    public BlockBehaviorAutoStashable(Block block) : base(block) { }

    /// <summary>Creates the interaction behavior with an explicit progress ownership service.</summary>
    internal BlockBehaviorAutoStashable(Block block, IProgressSystemProvider progressSystem) : base(block)
    {
        this.progressSystem = progressSystem;
    }

    /// <summary>Binds the engine API and supplies the default progress ownership service when needed.</summary>
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        this.api = api;
        progressSystem ??= new ProgressSystemProvider(api);
        hold = new HoldInteractionProgress(progressSystem, PreStashGracePeriodSeconds, "AutoStashing");
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

        hold!.Reset();
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        var assessment = AutoStashService.AssessBlock(byPlayer.InventoryManager, blockEntity, true,
            blockSel, byPlayer);
        if (!assessment.HasAvailableItems) return false;
        if (!assessment.CanStartInteraction) return true;
        handling = assessment.StartHandling;
        stashingState = EStashingState.PreStashGracePeriod;
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

        if (stashingState >= EStashingState.PostStashGracePeriod)
        {
            return true;// Return here so we don't keep trying to stash after we've already done it.
        }

        bool completed = hold!.UpdateElapsed(secondsUsed, StashDelaySeconds,
            VanillaExpandedModSystem.Config.AutoStashGuiRendersTopmost);
        if (secondsUsed >= PreStashGracePeriodSeconds)
        {
            stashingState = EStashingState.Stashing;
        }

        if (completed)
        {
            stashingState = EStashingState.PostStashGracePeriod;
            world.Api.ModLoader.GetModSystem<AutoStashSystem_Client>().RequestAutoStash(blockSel.Position);
            handleDidMoveItems(byPlayer.Entity);
        }

        return true;
    }

    public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        ResetGesture();
        return base.OnBlockInteractCancel(secondsUsed, world, byPlayer, blockSel, ref handling);
    }

    public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
    {
        ResetGesture();
        handling = EnumHandling.Handled;
    }
    #endregion

    #region Private
    internal bool HasActiveGesture(IWorldAccessor world)
        => world.Side == EnumAppSide.Client && stashingState != EStashingState.None;

    private void ResetGesture()
    {
        stashingState = EStashingState.None;
        hold?.Reset();
    }

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
            AutoStashToCrate(world, byPlayer.InventoryManager, crateEntity, byPlayer.PlayerName);
        }
        else if (be is BlockEntityBloomery bloomeryEntity)
        {
            AutoStashToBloomery(world, byPlayer.InventoryManager, bloomeryEntity, byPlayer.PlayerName);
        }
        else if (be is BlockEntityQuern quernEntity)
        {
            AutoStashService.StashQuern(world, byPlayer.InventoryManager, quernEntity, byPlayer.PlayerName);
        }
        else if (be is BlockEntityContainer containerEntity)
        {
            AutoStashToGenericContainer(world, byPlayer.InventoryManager, containerEntity, byPlayer.PlayerName);
        }
    }

    /// <summary>
    /// Handles the event when items have been moved into the container.
    /// </summary>
    private void handleDidMoveItems(EntityPlayer playerEntity)
    {
        IWorldAccessor world = playerEntity.World;
        //if (world.Side == EnumAppSide.Server)
        {
            world.PlaySoundAt(stashSoundPath, playerEntity, null, false, 16, volume: 1.0f);
        }

        if (api is ICoreClientAPI client)
        {
            client.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        }
    }
    #endregion

    #region World Interaction Help
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer, ref EnumHandling handling)
        => GetPlacedBlockInteractionHelp(world, selection, forPlayer.InventoryManager, ref handling);
    /// <summary>
    /// Gets interaction help from the inventory data required to determine stashable items.
    /// </summary>
    internal WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayerInventoryManager playerInventory,
        ref EnumHandling handling)
    {
        // Don't show interaction help if auto-stash is disabled
        if (!VanillaExpandedModSystem.Config.EnableAutoStash)
        {
            return [];
        }

        var assessment = AutoStashService.AssessBlock(playerInventory,
            world.BlockAccessor.GetBlockEntity(selection.Position), true, selection, forHelp: true);
        if (!assessment.CanStartInteraction || !assessment.HasCandidates)
            return [];

        switch (block.EntityClass)
        {
            case "Crate":
                {
                    return GetContainerInteractionHelp(world, selection, playerInventory, ["ctrl", "shift"]);
                }
            case "Bloomery":
                {
                    // If the player has no stashable items, do not show the interaction help.
                    ItemStack[]? stashableStacks = GetStashableItemStacks(world, playerInventory, selection);
                    if (stashableStacks is null || stashableStacks.Length == 0)
                    {
                        return [];
                    }

                    return [
                        new WorldInteraction()
                        {
                            ActionLangCode = "vanillaexpanded:blockhelp-autostash-bloomery",
                            MouseButton = EnumMouseButton.Right,
                            Itemstacks = stashableStacks,
                        }
                    ];
                }
            default:
                {
                    return GetContainerInteractionHelp(world, selection, playerInventory);
                }
        }
    }

    /// <summary>Retains container/full help actions and caller-selected modifiers using advisory capacity.</summary>
    private WorldInteraction[] GetContainerInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayerInventoryManager playerInventory,
        string[]? hotKeyCodes = null)
    {
        ItemStack[]? stashableStacks = GetStashableItemStacks(world, playerInventory, selection);
        string? actionLangCode = stashableStacks is { Length: > 0 }
            ? "vanillaexpanded:blockhelp-autostash-container"
            : HasContainerStashCandidates(world, playerInventory, selection)
                ? "vanillaexpanded:blockhelp-autostash-full"
                : null;

        return actionLangCode is null
            ? []
            : [
                new WorldInteraction()
                {
                    ActionLangCode = actionLangCode,
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCodes = hotKeyCodes,
                    Itemstacks = stashableStacks,
                }
            ];
    }

    /// <summary>
    /// Gets the actual ItemStack objects that can be stashed, for display in interaction help.
    /// </summary>
    private static ItemStack[]? GetStashableItemStacks(IWorldAccessor world, IPlayerInventoryManager playerInventory, BlockSelection? selection)
    {
        if (selection is null)
        {
            return null;
        }

        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
        if (blockEntity is null)
        {
            return null;
        }

        AutoStashAssessment assessment = AutoStashService.AssessBlock(playerInventory, blockEntity, true,
            selection, forHelp: true);
        return !assessment.CanStartInteraction || assessment.DisplayStacks.Length == 0 ? null : assessment.DisplayStacks;
    }
    #endregion

    #region AutoStashing Implementation

    /// <summary>
    /// Gets the item types which are present in both the player's inventory/hotbar AND can be stashed into the specified block entity.
    /// Handles both containers and bloomeries, including state validation (e.g., bloomery not burning, output empty).
    /// </summary>
    /// <param name="playerInventory"> The inventory/hotbar to check </param>
    /// <param name="blockEntity"> The block entity to check (container or bloomery) </param>
    /// <returns> A set of collectible IDs which can be stashed from the player into the block entity. </returns>
    internal static HashSet<int> GetStashableItems(IPlayerInventoryManager playerInventory, BlockEntity? blockEntity)
    {
        var assessment = AssessTarget(playerInventory, blockEntity);
        return assessment.CanStartInteraction ? [.. assessment.AvailableItemIds] : [];
    }

    /// <summary>Assesses target policy without requiring a currently held interaction gesture.</summary>
    private static AutoStashAssessment AssessTarget(IPlayerInventoryManager owner, BlockEntity? blockEntity)
        => AutoStashService.AssessBlock(owner, blockEntity, true, forHelp: true, checkInteraction: true);

    /// <summary>
    /// Automatically stashes items from the player's inventory into the specified generic container.
    /// The item-types which are already present in the container are the ones which will be stashed.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="playerInventory"></param>
    /// <param name="playerName"></param>
    /// <param name="container"></param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    public static bool AutoStashToGenericContainer(
        in IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        in BlockEntityContainer container,
        string playerName = "")
    {
        return AutoStashService.StashContainer(world, playerInventory, container, playerName, crate: false).MovedQuantity > 0;
    }

    /// <summary>
    /// Automatically stashes items from the player's inventory into the specified crate container.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="playerInventory"></param>
    /// <param name="playerName"></param>
    /// <param name="container"></param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    public static bool AutoStashToCrate(
        in IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        in BlockEntityCrate container,
        string playerName = "")
    {
        return AutoStashService.StashContainer(world, playerInventory, container, playerName, crate: true).MovedQuantity > 0;
    }

    /// <summary>
    /// Automatically stashes items from the player's inventory into the specified bloomery.
    /// Stashes valid fuel to slot 0 and valid ore to slot 1, respecting bloomery capacity limits.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="playerInventory"></param>
    /// <param name="playerName"></param>
    /// <param name="bloomery"></param>
    /// <returns>True if any items were stashed, false otherwise.</returns>
    public static bool AutoStashToBloomery(
        IWorldAccessor world,
        IPlayerInventoryManager playerInventory,
        BlockEntityBloomery bloomery,
        string playerName = "")
    {
        return AutoStashService.StashBloomery(world, playerInventory, bloomery, playerName).MovedQuantity > 0;
    }

    #endregion

    #region Private Implementation

    private bool HasStashables(in IWorldAccessor world, IPlayerInventoryManager playerInventory, BlockSelection selection)
    {
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
        HashSet<int> stashables = GetStashableItems(playerInventory, blockEntity);
        return stashables.Count != 0;
    }

    /// <summary>Distinguishes matching contents from apparent capacity without acquiring the target.</summary>
    private bool HasContainerStashCandidates(in IWorldAccessor world, IPlayerInventoryManager playerInventory, BlockSelection selection)
    {
        BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(selection.Position);
        return blockEntity is BlockEntityContainer container
            && AssessTarget(playerInventory, container).HasCandidates;
    }
    #endregion
}
