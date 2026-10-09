using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing;

/// <summary>Handles attached-container interaction, matching assessment and persistent engine transfers.</summary>
internal static class EntityAttachedContainerAutoStash
{
    #region Public API
    #region Interaction and help
    /// <summary>Begins the client-owned gesture after mounted-control, selection and candidate gates pass.</summary>
    public static bool HandleInteract(
        EntityBehaviorAttachable attachable,
        EntityAgent byEntity,
        EnumInteractMode mode,
        ref EnumHandling handled)
    {
        EntityControls controls = byEntity.MountedOn?.Controls ?? byEntity.Controls;
        if (mode != EnumInteractMode.Interact
            || !VanillaExpandedModSystem.Config.EnableAutoStash
            || byEntity.World.Side != EnumAppSide.Client
            || byEntity is not EntityPlayer playerEntity
            || !controls.CtrlKey
            || !controls.ShiftKey)
        {
            return true;
        }

        int selectionBoxIndex = playerEntity.EntitySelection?.SelectionBoxIndex ?? -1;
        int attachmentSlotIndex = selectionBoxIndex > 0
            ? attachable.GetSlotIndexFromSelectionBoxIndex(selectionBoxIndex - 1)
            : -1;
        if (attachmentSlotIndex < 0
            || !CanAutoStash(playerEntity.Player.InventoryManager, attachable.Inventory[attachmentSlotIndex], byEntity.World))
        {
            return true;
        }

        EntityAttachedContainerAutoStashClient? client = byEntity.World.Api.ModLoader
            .GetModSystem<AutoStashSystem_Client>()?.EntityAttachedContainers;
        if (client is null)
        {
            return true;
        }

        client.Begin(attachable, attachmentSlotIndex);
        handled = EnumHandling.PreventSubsequent;
        return false;
    }

    /// <summary>Appends the existing Ctrl+Shift action without altering prior interactions or constructing bag workspaces.</summary>
    public static void AppendInteractionHelp(
        EntityBehaviorAttachable attachable,
        IClientWorldAccessor world,
        EntitySelection selection,
        IClientPlayer player,
        ref WorldInteraction[] interactions)
    {
        if (!VanillaExpandedModSystem.Config.EnableAutoStash)
        {
            return;
        }

        int attachmentSlotIndex = selection.SelectionBoxIndex > 0
            ? attachable.GetSlotIndexFromSelectionBoxIndex(selection.SelectionBoxIndex - 1)
            : -1;
        if (attachmentSlotIndex < 0
            || !CanAutoStash(player.InventoryManager, attachable.Inventory[attachmentSlotIndex], world))
        {
            return;
        }

        WorldInteraction autoStashInteraction = new()
        {
            ActionLangCode = "vanillaexpanded:blockhelp-autostash-container",
            MouseButton = EnumMouseButton.Right,
            HotKeyCodes = ["ctrl", "shift"]
        };
        interactions = [.. interactions, autoStashInteraction];
    }

    /// <summary>Retains matching-only client eligibility while shared assessment explicitly leaves bag capacity unknown.</summary>
    public static bool CanAutoStash(IPlayerInventoryManager playerInventory, ItemSlot attachmentSlot, IWorldAccessor world)
        => AutoStashService.AssessAttached(world, playerInventory, attachmentSlot).HasCandidates;
    #endregion

    #region Server execution
    /// <summary>Transfers matching contents and persists applied or uncertain changes before releasing an owned workspace session.</summary>
    public static bool TryAutoStash(
        IWorldAccessor world,
        IPlayer player,
        Entity hostEntity,
        EntityBehaviorAttachable attachable,
        int attachmentSlotIndex)
    {
        return AutoStashService.StashAttached(world, player, hostEntity, attachable, attachmentSlotIndex).MovedQuantity > 0;
    }
    #endregion
    #endregion
}
