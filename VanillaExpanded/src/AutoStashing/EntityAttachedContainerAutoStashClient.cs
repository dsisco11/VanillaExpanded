using System;

using VanillaExpanded.RadialProgress;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoStashing;

internal sealed class EntityAttachedContainerAutoStashClient : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Action<long, int> requestAutoStash;
    private readonly long tickListenerId;
    private long pendingEntityId;
    private int pendingAttachmentSlotIndex = -1;
    private float pendingSeconds;
    private bool requestSent;
    private IRadialProgressBar? progressBar;

    public EntityAttachedContainerAutoStashClient(ICoreClientAPI api, Action<long, int> requestAutoStash)
    {
        this.api = api;
        this.requestAutoStash = requestAutoStash;
        tickListenerId = api.Event?.RegisterGameTickListener(OnGameTick, 20) ?? 0;
    }

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

    public void Dispose()
    {
        Cancel();
        if (api.Event is not null && tickListenerId != 0)
        {
            api.Event.UnregisterGameTickListener(tickListenerId);
        }
    }

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

        if (requestSent)
        {
            return;
        }

        pendingSeconds += deltaTime;
        if (pendingSeconds >= BlockBehaviorAutoStashable.PreStashGracePeriodSeconds)
        {
            progressBar ??= api.ModLoader.GetModSystem<ModSystemRadialProgressBar>()?.AddProgressBar();
            if (progressBar is not null)
            {
                progressBar.Progress = Math.Clamp(pendingSeconds / VanillaExpandedModSystem.Config.AutoStashDelay, 0f, 1f);
                progressBar.Text = "AutoStashing";
            }
        }

        if (pendingSeconds < VanillaExpandedModSystem.Config.AutoStashDelay)
        {
            return;
        }

        requestSent = true;
        requestAutoStash(pendingEntityId, pendingAttachmentSlotIndex);
        RemoveProgressBar();
        api.World.PlaySoundAt(new AssetLocation("game:sounds/player/poultice-applied"), player.Entity, null, false, 16, volume: 1f);
        player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
    }

    private void Cancel()
    {
        pendingEntityId = 0;
        pendingAttachmentSlotIndex = -1;
        pendingSeconds = 0;
        requestSent = false;
        RemoveProgressBar();
    }

    private void RemoveProgressBar()
    {
        if (progressBar is null)
        {
            return;
        }

        api.ModLoader.GetModSystem<ModSystemRadialProgressBar>()?.RemoveProgressBar(progressBar);
        progressBar = null;
    }
}