using System;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Lifecycle;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.HudOverlays;

/// <summary>Connects client world/configuration events to the owning HUD runtime and built-in group registrations.</summary>
public sealed class HudOverlaySystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? api;
    private HudOverlaySession? session;
    private HudOverlayEditInput? editInput;
    private bool disposed;
    /// <summary>Gets the shared registry for client overlay and group registration.</summary>
    public HudOverlayRegistry Registry { get; } = new();
    internal HudOverlaySession? Session => session;
    internal HudOverlayIconBackdrop? Backdrop { get; private set; }

    #region Public API
    #region Integration
    /// <summary>Requests a coalesced content refresh on the client main thread; inactive sessions need no invalidation.</summary>
    public void Invalidate(string overlayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayId);
        ObjectDisposedException.ThrowIf(disposed, this);
        session?.Scheduler?.Invalidate(overlayId);
    }

    /// <summary>Reevaluates live visibility after integration settings change, without sampling during the callback.</summary>
    public void ConfigurationChanged()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        editInput?.Synchronize();
        session?.ConfigurationChanged();
    }
    #endregion
    #region Mod lifecycle
    /// <summary>Loads the passive overlay runtime on clients only.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Registers shared defaults and connects world events without creating presentation resources.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ObjectDisposedException.ThrowIf(disposed, this);
        this.api = api;
        editInput = new HudOverlayEditInput(api);
        Backdrop = new HudOverlayIconBackdrop(api);
        Registry.RegisterGroup(new HudOverlayGroup("held-item-status",
            HudOverlayConfiguration.ResolveHeldItemStatusPlacement(VanillaExpandedModSystem.Config)));
        Registry.RegisterGroup(new HudOverlayGroup("player-status",
            new HudOverlayPlacement(HudOverlayTargets.Hotbar,
                HudOverlayPoint.LeftBottom, HudOverlayPoint.RightBottom, 0, 4),
            new HudOverlayPacking(HudOverlayDirection.Horizontal, crossAlignment: HudOverlayCrossAlignment.End)));
        Registry.RegisterGroup(new HudOverlayGroup("world-status",
            new HudOverlayPlacement(HudOverlayTargets.Screen,
                HudOverlayPoint.RightTop, HudOverlayPoint.RightTop, screenPaddingPixels: 3),
            new HudOverlayPacking(HudOverlayDirection.Vertical, padding: 0, crossAlignment: HudOverlayCrossAlignment.End)));
        session = new HudOverlaySession(api, Registry, enabled: () => VanillaExpandedModSystem.Config.EnableHudOverlays);
        api.Event.LevelFinalize += session.EnterWorld;
        api.Event.LeaveWorld += session.LeaveWorld;
    }

    /// <summary>Applies live selectors immediately without forcing gameplay samples or texture preparation.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (disposed || !ReferenceEquals(this.api, api)) return;
        foreach (var group in Registry.GetGroups())
        {
            if (group.Id != "held-item-status") continue;
            Registry.UpdateGroup(new HudOverlayGroup(group.Id,
                HudOverlayConfiguration.ResolveHeldItemStatusPlacement(VanillaExpandedModSystem.Config), group.Packing));
            break;
        }
        editInput?.Synchronize();
        session?.ConfigurationChanged();
    }

    /// <summary>Detaches world events and releases session resources before final registration ownership.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (api != null && session != null)
        {
            api.Event.LevelFinalize -= session.EnterWorld;
            api.Event.LeaveWorld -= session.LeaveWorld;
        }
        try
        {
            try { editInput?.Dispose(); }
            finally { session?.Dispose(); }
        }
        finally
        {
            session = null;
            editInput = null;
            api = null;
            try { Backdrop?.Dispose(); }
            finally
            {
                Backdrop = null;
                try { Registry.Dispose(); }
                finally { base.Dispose(); }
            }
        }
    }
    #endregion
    #endregion
}
