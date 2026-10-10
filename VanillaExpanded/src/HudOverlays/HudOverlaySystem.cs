using System;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Lifecycle;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.HudOverlays;

/// <summary>Connects client world/configuration events to the owning HUD runtime and built-in group registrations.</summary>
internal sealed class HudOverlaySystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? api;
    private HudOverlaySession? session;
    private bool disposed;
    public HudOverlayRegistry Registry { get; } = new();
    public HudOverlaySession? Session => session;

    #region Public API
    /// <summary>Loads the passive overlay runtime on clients only.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Registers shared defaults and connects world events without creating presentation resources.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ObjectDisposedException.ThrowIf(disposed, this);
        this.api = api;
        Registry.RegisterGroup(new HudOverlayGroup("held-item-status", new HudOverlayPlacement(
            HudOverlayAnchorContext.SaturationTargetId, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftBottom, -4, 0)));
        session = new HudOverlaySession(api, Registry);
        api.Event.LevelFinalize += session.EnterWorld;
        api.Event.LeaveWorld += session.LeaveWorld;
    }

    /// <summary>Applies live selectors immediately without forcing gameplay samples or texture preparation.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (ReferenceEquals(this.api, api)) session?.ConfigurationChanged();
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
        try { session?.Dispose(); }
        finally
        {
            session = null;
            api = null;
            try { Registry.Dispose(); }
            finally { base.Dispose(); }
        }
    }
    #endregion
}
