using System;

using VanillaExpanded.RadialProgress;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing;

/// <summary>Owns the attached-container input gesture, progress display and single client request.</summary>
internal sealed class EntityAttachedContainerAutoStashClient : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Action<long, int> requestAutoStash;
    private readonly long tickListenerId;
    private long pendingEntityId;
    private int pendingAttachmentSlotIndex = -1;
    private readonly HoldInteractionProgress hold;

    #region Public API
    /// <summary>Registers gesture ticks with the supplied or default progress ownership service.</summary>
    public EntityAttachedContainerAutoStashClient(ICoreClientAPI api, Action<long, int> requestAutoStash,
        IProgressSystemProvider? progressSystem = null)
    {
        this.api = api;
        this.requestAutoStash = requestAutoStash;
        hold = new HoldInteractionProgress(progressSystem ?? new ProgressSystemProvider(api),
            BlockBehaviorAutoStashable.PreStashGracePeriodSeconds, "AutoStashing");
        tickListenerId = api.Event?.RegisterGameTickListener(OnGameTick, 20) ?? 0;
    }

    /// <summary>Starts a target gesture, preserving an existing gesture for the same target.</summary>
    public void Begin(EntityBehaviorAttachable attachable, int attachmentSlotIndex)
    {
        if (pendingEntityId == attachable.entity.EntityId
            && pendingAttachmentSlotIndex == attachmentSlotIndex)
        {
            return;
        }

        Cancel();
        pendingEntityId = attachable.entity.EntityId;
        pendingAttachmentSlotIndex = attachmentSlotIndex;
    }

    /// <summary>Cancels pending presentation and unregisters the owned tick listener.</summary>
    public void Dispose()
    {
        Cancel();
        if (api.Event is not null && tickListenerId != 0)
        {
            api.Event.UnregisterGameTickListener(tickListenerId);
        }
    }

    #endregion

    #region Private
    /// <summary>Validates held input and target selection before advancing time and submitting once.</summary>
    private void OnGameTick(float deltaTime)
    {
        if (api.World.Player is not IClientPlayer player || pendingAttachmentSlotIndex < 0)
        {
            return;
        }

        if (!VanillaExpandedModSystem.Config.EnableAutoStash)
        {
            Cancel();
            return;
        }

        EntitySelection? selection = player.CurrentEntitySelection;
        EntityBehaviorAttachable? attachable = selection?.Entity.GetBehavior<EntityBehaviorAttachable>();
        int selectedSlotIndex = selection?.SelectionBoxIndex > 0
            ? attachable?.GetSlotIndexFromSelectionBoxIndex(selection.SelectionBoxIndex - 1) ?? -1
            : -1;
        EntityControls controls = player.Entity.MountedOn?.Controls ?? player.Entity.Controls;
        if (!api.Input.InWorldMouseButton.Right
            || !controls.CtrlKey
            || !controls.ShiftKey
            || selection?.Entity.EntityId != pendingEntityId
            || selectedSlotIndex != pendingAttachmentSlotIndex)
        {
            Cancel();
            return;
        }

        if (!hold.Advance(deltaTime, VanillaExpandedModSystem.Config.AutoStashDelay,
            VanillaExpandedModSystem.Config.AutoStashGuiRendersTopmost))
        {
            return;
        }

        requestAutoStash(pendingEntityId, pendingAttachmentSlotIndex);
        api.World.PlaySoundAt(new AssetLocation("game:sounds/player/poultice-applied"), player.Entity, null, false, 16, volume: 1f);
        player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
    }

    /// <summary>Clears elapsed gesture state and its progress display.</summary>
    private void Cancel()
    {
        pendingEntityId = 0;
        pendingAttachmentSlotIndex = -1;
        hold.Reset();
    }
    #endregion
}
