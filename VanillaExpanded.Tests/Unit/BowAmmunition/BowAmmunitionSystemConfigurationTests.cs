using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Configuration;
using Vintagestory.API.Common;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.BowAmmunition;

/// <summary>Exercises real configuration composition roots without graphics or gameplay mutation.</summary>
[Collection("HudOverlayGeometry")]
public sealed class BowAmmunitionSystemConfigurationTests
{
    #region Public API
    /// <summary>Both toggles stop the registered bow's subscriptions immediately and re-enable with fresh state.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadDisablesRegisteredFeatureImmediately(bool global)
    {
        var config = VanillaExpandedModSystem.Config;
        bool previousGlobal = config.EnableHudOverlays, previousBow = config.EnableBowAmmunitionOverlay;
        string previousAnchor = config.HeldItemStatusAnchor;
        var hud = new HudOverlaySystem();
        var system = new VanillaExpanded.BowAmmunition.BowAmmunitionSystem();
        try
        {
            config.EnableHudOverlays = config.EnableBowAmmunitionOverlay = true;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object);
            system.StartClientSide(fixture.Api.Object);
            hud.Registry.BeginSession(fixture.Api.Object);
            VanillaExpanded.BowAmmunition.BowAmmunitionOverlay? overlay = null;
            hud.Registry.RunPass(registration => overlay = Assert.IsType<VanillaExpanded.BowAmmunition.BowAmmunitionOverlay>(registration.Overlay));
            fixture.Personal[0].Itemstack = new ItemStack(fixture.Flint, 12);
            overlay!.Refresh(); Assert.Equal(12, overlay.Sample!.Quantity);
            if (global) config.EnableHudOverlays = false; else config.EnableBowAmmunitionOverlay = false;
            system.OnConfigReloaded(fixture.Api.Object); Assert.Null(overlay.Sample);
            fixture.Events.VerifyRemove(value => value.AfterActiveSlotChanged -= It.IsAny<Action<ActiveSlotChangeEventArgs>>(), Times.Once);
            config.EnableHudOverlays = config.EnableBowAmmunitionOverlay = true;
            system.OnConfigReloaded(fixture.Api.Object); Assert.Null(overlay.Sample);
            fixture.Personal[0].Itemstack!.StackSize = 5; overlay.Refresh(); Assert.Equal(5, overlay.Sample!.Quantity);
        }
        finally { system.Dispose(); hud.Dispose(); config.EnableHudOverlays = previousGlobal; config.EnableBowAmmunitionOverlay = previousBow; config.HeldItemStatusAnchor = previousAnchor; }
    }

    /// <summary>Repeated settings reloads update only the registered placement and retain packing defaults.</summary>
    [Fact]
    public void HudSystemReloadAppliesCurrentPersistedPlacement()
    {
        var config = VanillaExpandedModSystem.Config;
        string oldAnchor = config.HeldItemStatusAnchor; float oldX = config.HeldItemStatusOffsetX, oldY = config.HeldItemStatusOffsetY;
        var hud = new HudOverlaySystem();
        try
        {
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            hud.StartClientSide(fixture.Api.Object); var packing = hud.Registry.GetGroups().Single().Packing;
            foreach (string key in new[] { "hotbar", "screen-right-bottom", "saturation", "screen-left-top" })
            {
                config.HeldItemStatusAnchor = key; config.HeldItemStatusOffsetX = 8; config.HeldItemStatusOffsetY = -12;
                hud.OnConfigReloaded(fixture.Api.Object); hud.OnConfigReloaded(fixture.Api.Object);
                var group = hud.Registry.GetGroups().Single();
                Assert.Equal(HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config), group.Placement);
                Assert.Same(packing, group.Packing);
            }
        }
        finally { hud.Dispose(); config.HeldItemStatusAnchor = oldAnchor; config.HeldItemStatusOffsetX = oldX; config.HeldItemStatusOffsetY = oldY; }
    }
    #endregion
}
