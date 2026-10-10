using System;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Registers the feature-owned bow indicator after the shared runtime's group defaults exist.</summary>
internal sealed class BowAmmunitionSystem : ModSystem
{
    internal const string OverlayId = "vanillaexpanded:bow-ammunition";
    private IDisposable? registration;
    #region Public API
    /// <summary>Loads ammunition presentation on clients only.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    /// <summary>Runs after the shared HUD runtime's default execution order.</summary>
    public override double ExecuteOrder() => 1;
    /// <summary>Transfers feature lifetime ownership to the shared registry with a bounded fallback interval.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        HudOverlaySystem hud = api.ModLoader.GetModSystem<HudOverlaySystem>();
        var overlay = new BowAmmunitionOverlay(() => hud.Session?.Scheduler?.Invalidate(OverlayId));
        try
        {
            registration = hud.Registry.Register(new HudOverlayRegistration(OverlayId, overlay,
                () => overlay.SetEnabled(true), "held-item-status", refreshIntervalMs: 250));
        }
        catch { overlay.Dispose(); throw; }
    }
    /// <summary>Removes the registration through its idempotent owning handle.</summary>
    public override void Dispose()
    {
        registration?.Dispose(); registration = null;
        base.Dispose();
    }
    #endregion
}
