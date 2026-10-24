using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.WindSpeed;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.WindSpeed;

/// <summary>Checks actual registration and live enable composition without loading graphics services.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WindSpeedSystemTests
{
    #region Public API
    /// <summary>Feature and global toggles immediately disable the registered overlay, retaining its screen placement.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveToggleAndNativeAnchor(bool global)
    {
        var config = VanillaExpandedModSystem.Config;
        bool previousGlobal = config.EnableHudOverlays, previousWind = config.EnableWindSpeedOverlay,
            previousWeather = config.EnableWeatherConditionsOverlay;
        var hud = new HudOverlaySystem();
        var system = new WindSpeedSystem();
        try
        {
            config.EnableHudOverlays = config.EnableWindSpeedOverlay = true;
            config.EnableWeatherConditionsOverlay = false;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            fixture.Entity.Alive = true;
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object);
            system.StartClientSide(fixture.Api.Object);
            var group = hud.Registry.GetGroups().Single(value => value.Id == "player-status");
            Assert.Equal(HudOverlayTargets.Hotbar, group.Placement.TargetId);
            Assert.Equal(HudOverlayPoint.LeftBottom, group.Placement.Attachment);
            Assert.Equal(HudOverlayPoint.RightBottom, group.Placement.Pivot);
            Assert.Equal(HudOverlayDirection.Horizontal, group.Packing.Direction);
            Assert.Equal(HudOverlayCrossAlignment.End, group.Packing.CrossAlignment);

            Assert.True(group.DefaultMovable);

            hud.Registry.BeginSession(fixture.Api.Object);
            WindSpeedOverlay? overlay = null;
            hud.Registry.RunPass(registration =>
            {
                Assert.Equal(WindSpeedSystem.OverlayId, registration.Id);
                Assert.Equal(300, registration.RefreshIntervalMs);
                Assert.Null(registration.ReservedSize);
                Assert.Equal(96, registration.Order);
                Assert.Equal("player-status", registration.GroupId);
                overlay = Assert.IsType<WindSpeedOverlay>(registration.Overlay);
            });
            Assert.True(overlay!.IsApplicable());
            config.EnableWeatherConditionsOverlay = true;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.True(overlay.IsApplicable());
            if (global) config.EnableHudOverlays = false; else config.EnableWindSpeedOverlay = false;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.False(overlay.IsApplicable());
            Assert.Equal(0, overlay.Intensity);
            config.EnableHudOverlays = config.EnableWindSpeedOverlay = true;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.True(overlay.IsApplicable());
            Assert.True(system.ShouldLoad(EnumAppSide.Client));
            Assert.False(system.ShouldLoad(EnumAppSide.Server));
        }
        finally
        {
            system.Dispose();
            hud.Dispose();
            config.EnableHudOverlays = previousGlobal;
            config.EnableWindSpeedOverlay = previousWind;
            config.EnableWeatherConditionsOverlay = previousWeather;
        }
    }
    #endregion
}
