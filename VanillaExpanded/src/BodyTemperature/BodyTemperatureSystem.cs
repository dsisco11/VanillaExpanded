using System;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Registers the feature-owned body temperature indicator after the shared runtime's group defaults exist.</summary>
internal sealed class BodyTemperatureSystem : ModSystem, ILiveConfigurable
{
    internal const string OverlayId = "vanillaexpanded:body-temperature";
    private IDisposable? registration;
    private ICoreClientAPI? api;
    private HudOverlaySystem? hud;
    private BodyTemperatureOverlay? overlay;
    private bool disposed;
    #region Public API
    /// <summary>Loads temperature presentation on clients only.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    /// <summary>Runs after the shared HUD runtime's default execution order.</summary>
    public override double ExecuteOrder() => 1;
    /// <summary>Transfers feature lifetime ownership to the shared registry with a bounded fallback interval.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ObjectDisposedException.ThrowIf(disposed, this);
        this.api = api;
        hud = api.ModLoader.GetModSystem<HudOverlaySystem>();
        overlay = new BodyTemperatureOverlay();
        overlay.SetShowReading(VanillaExpandedModSystem.Config.ShowBodyTemperatureReading);
        overlay.SetEnabled(VanillaExpandedModSystem.Config.EnableHudOverlays && VanillaExpandedModSystem.Config.EnableBodyTemperatureOverlay);
        try
        {
            registration = hud.Registry.Register(new HudOverlayRegistration(OverlayId, overlay,
                () => overlay.SetEnabled(VanillaExpandedModSystem.Config.EnableHudOverlays
                    && VanillaExpandedModSystem.Config.EnableBodyTemperatureOverlay), "player-status", refreshIntervalMs: 250));
        }
        catch { overlay.Dispose(); throw; }
    }
    /// <summary>Immediately suspends feature resources and synchronizes cached layout for live enable changes.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (disposed || !ReferenceEquals(this.api, api) || overlay == null) return;
        overlay.SetEnabled(VanillaExpandedModSystem.Config.EnableHudOverlays && VanillaExpandedModSystem.Config.EnableBodyTemperatureOverlay);
        // Cleanup must also occur while global visibility suppresses normal selector observation.
        overlay.SetShowReading(VanillaExpandedModSystem.Config.ShowBodyTemperatureReading);
        hud?.Session?.Scheduler?.Invalidate(OverlayId);
        hud?.Session?.ConfigurationChanged();
    }
    /// <summary>Removes the registration through its idempotent owning handle.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { registration?.Dispose(); }
        finally
        {
            registration = null; overlay = null; hud = null; api = null;
            base.Dispose();
        }
    }
    #endregion
}
