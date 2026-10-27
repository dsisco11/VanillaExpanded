using System;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Shelter;

internal sealed class ShelterSystem : ModSystem, ILiveConfigurable
{
    internal const string OverlayId = "vanillaexpanded:shelter";
    private IDisposable? registration;
    private ICoreClientAPI? api;
    private HudOverlaySystem? hud;
    private ShelterOverlay? overlay;
    private bool disposed;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override double ExecuteOrder() => 1;

    public override void StartClientSide(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ObjectDisposedException.ThrowIf(disposed, this);
        this.api = api;
        hud = api.ModLoader.GetModSystem<HudOverlaySystem>();
        overlay = new ShelterOverlay();
        overlay.SetStyle(VanillaExpandedModSystem.Config.ShelterIndicatorStyle);
        overlay.SetEnabled(VanillaExpandedModSystem.Config.EnableHudOverlays && VanillaExpandedModSystem.Config.EnableShelterOverlay);
        try
        {
            registration = hud.Registry.Register(new HudOverlayRegistration(OverlayId, overlay,
                () => overlay.SetEnabled(VanillaExpandedModSystem.Config.EnableHudOverlays
                    && VanillaExpandedModSystem.Config.EnableShelterOverlay), "player-status", order: 95, refreshIntervalMs: 300,
                hoverLabelKey: "vanillaexpanded:hud-label-shelter",
                menuSettings: new HudOverlayMenuSettings(
                    new HudOverlaySetting<bool>(() => VanillaExpandedModSystem.Config.EnableShelterOverlay,
                        value => VanillaExpandedModSystem.Config.EnableShelterOverlay = value, () => HudMenuConfiguration.Commit(api)),
                    new HudOverlayStyleSetting<HudIndicatorStyle>(() => VanillaExpandedModSystem.Config.ShelterIndicatorStyle,
                        value => VanillaExpandedModSystem.Config.ShelterIndicatorStyle = value, () => HudMenuConfiguration.Commit(api)))));
        }
        catch { overlay.Dispose(); throw; }
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (disposed || !ReferenceEquals(this.api, api) || overlay == null) return;
        overlay.SetStyle(VanillaExpandedModSystem.Config.ShelterIndicatorStyle);
        overlay.SetEnabled(VanillaExpandedModSystem.Config.EnableHudOverlays && VanillaExpandedModSystem.Config.EnableShelterOverlay);
        hud?.Session?.Scheduler?.Invalidate(OverlayId);
        hud?.Session?.ConfigurationChanged();
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { registration?.Dispose(); }
        finally
        {
            registration = null;
            overlay = null;
            hud = null;
            api = null;
            base.Dispose();
        }
    }
}
