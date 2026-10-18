using System;
using VanillaExpanded.RadialProgress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoLoot;

internal sealed class AutoLootClient : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly HoldInteractionProgress hold;
    private readonly long tickListenerId;
    private AutoLootTarget? pending;
    private bool requiresRelease;
    private bool disposed;

    public AutoLootClient(ICoreClientAPI api, IProgressSystemProvider? progressSystem = null)
    {
        this.api = api;
        hold = new HoldInteractionProgress(progressSystem ?? new ProgressSystemProvider(api),
            0.1f, Lang.Get("vanillaexpanded:autoloot-progress"));
        tickListenerId = api.Event.RegisterGameTickListener(OnGameTick, 20);
        api.Event.MouseUp += OnMouseUp;
        api.Event.LeaveWorld += OnLeaveWorld;
    }

    public void OnInteract(EntityBehaviorHarvestable behavior, GuiDialog? dialog,
        EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode)
    {
        if (disposed || api.World.Player is not IClientPlayer player
            || !ReferenceEquals(player.Entity, byEntity) || api.Side != EnumAppSide.Client)
        {
            return;
        }

        if (pending is not null && !ReferenceEquals(pending.Corpse, behavior.entity))
        {
            Cancel();
        }

        if (requiresRelease || mode != EnumInteractMode.Interact
            || !VanillaExpandedModSystem.Config.EnableAutoLoot || api.IsGamePaused
            || !api.Input.MouseButton.Right)
        {
            return;
        }

        if (!AutoLootTarget.TryCreate(api, player, behavior, dialog, hitPosition, out AutoLootTarget? target))
        {
            return;
        }

        hold.Reset();
        pending = target;
        requiresRelease = true;
        target.Dialog.OnClosed += Cancel;
    }

    public void Cancel()
    {
        AutoLootTarget? target = pending;
        pending = null;
        if (target is not null)
        {
            target.Dialog.OnClosed -= Cancel;
        }
        hold.Reset();
    }

    public void CancelUntilRelease()
    {
        requiresRelease = true;
        Cancel();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        api.Event.UnregisterGameTickListener(tickListenerId);
        api.Event.MouseUp -= OnMouseUp;
        api.Event.LeaveWorld -= OnLeaveWorld;
        Cancel();
    }

    private void OnGameTick(float deltaTime)
    {
        if (disposed)
        {
            return;
        }
        if (!api.Input.MouseButton.Right)
        {
            requiresRelease = false;
            Cancel();
            return;
        }
        if (pending is not AutoLootTarget target)
        {
            return;
        }
        if (api.World.Player is not IClientPlayer player || !CanContinue(target, player) || !target.HasLoot)
        {
            Cancel();
            return;
        }

        try
        {
            if (hold.Advance(deltaTime, VanillaExpandedModSystem.Config.AutoLootDelay, rendersTopmost: true))
            {
                AutoLootTransferCoordinator.Collect(api, player, target, () => CanContinue(target, player));
            }
        }
        catch (Exception exception)
        {
            Cancel();
            api.Logger.Error("[AutoLoot] Collection failed for entity {0}: {1}", target.EntityId, exception);
        }
    }

    private bool CanContinue(AutoLootTarget target, IClientPlayer player)
    {
        return !disposed && ReferenceEquals(pending, target)
            && VanillaExpandedModSystem.Config.EnableAutoLoot
            && !api.IsGamePaused && api.Input.MouseButton.Right
            && (!api.Input.MouseGrabbed || player.CurrentEntitySelection?.Entity == target.Corpse)
            && target.IsValid(api, player);
    }

    private void OnMouseUp(MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Right)
        {
            return;
        }

        requiresRelease = false;
        Cancel();
    }

    private void OnLeaveWorld()
    {
        CancelUntilRelease();
    }
}
