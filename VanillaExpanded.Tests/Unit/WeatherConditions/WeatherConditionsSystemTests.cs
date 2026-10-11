using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.WeatherConditions;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WeatherConditions;

/// <summary>Checks actual registration and live enable composition without loading graphics services.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WeatherConditionsSystemTests
{
    #region Public API
    /// <summary>Feature and global toggles immediately disable the registered overlay, retaining its toolbar anchor.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveToggleAndNativeAnchor(bool global)
    {
        var config = VanillaExpandedModSystem.Config;
        bool previousGlobal = config.EnableHudOverlays, previousWeather = config.EnableWeatherConditionsOverlay;
        var hud = new HudOverlaySystem();
        var system = new WeatherConditionsSystem();
        try
        {
            config.EnableHudOverlays = config.EnableWeatherConditionsOverlay = true;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            fixture.Entity.Alive = true;
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            loader.Setup(value => value.GetModSystem<WeatherSystemClient>(true)).Returns(new WeatherSystemClient());
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object);
            system.StartClientSide(fixture.Api.Object);
            var group = hud.Registry.GetGroups().Single(value => value.Id == "weather-status");
            Assert.Equal(HudOverlayAnchorContext.HotbarTargetId, group.Placement.TargetId);
            Assert.Equal(HudOverlayPoint.RightBottom, group.Placement.Attachment);
            Assert.Equal(HudOverlayPoint.LeftBottom, group.Placement.Pivot);
            hud.Registry.BeginSession(fixture.Api.Object);
            WeatherConditionsOverlay? overlay = null;
            hud.Registry.RunPass(registration =>
            {
                Assert.Equal(WeatherConditionsSystem.OverlayId, registration.Id);
                Assert.Equal(1000, registration.RefreshIntervalMs);
                overlay = Assert.IsType<WeatherConditionsOverlay>(registration.Overlay);
            });
            Assert.True(overlay!.IsApplicable());
            if (global) config.EnableHudOverlays = false; else config.EnableWeatherConditionsOverlay = false;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.False(overlay.IsApplicable());
            Assert.Null(overlay.Sample);
            config.EnableHudOverlays = config.EnableWeatherConditionsOverlay = true;
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
            config.EnableWeatherConditionsOverlay = previousWeather;
        }
    }
    #endregion
}
