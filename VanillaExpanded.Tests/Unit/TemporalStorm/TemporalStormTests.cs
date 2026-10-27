using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.TemporalStorm;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.TemporalStorm;

/// <summary>Checks native schedule interpretation and passive storm presentation without graphics operations.</summary>
[Collection("HudOverlayGeometry")]
public sealed class TemporalStormTests
{
    #region Public API
    /// <summary>Warning thresholds retain approaching through the native notification gap.</summary>
    [Theory]
    [InlineData(.351, null)]
    [InlineData(.35, 0)]
    [InlineData(.03, 0)]
    [InlineData(.025, 0)]
    [InlineData(.02001, 0)]
    [InlineData(.02, 1)]
    [InlineData(0, 1)]
    [InlineData(-.1, 1)]
    public void ScheduledWarningsRemainContinuous(double remaining, int? expected)
    {
        var storms = new SystemTemporalStability();
        storms.StormData.nextStormTotalDays = remaining;
        Assert.Equal(expected, (int?)TemporalStormSource.Read(storms, 0));
    }

    /// <summary>Only synchronized activity decides the active stage, including waning storms.</summary>
    [Fact]
    public void ActiveOverridesWarningsAndInvalidSchedulesAreHidden()
    {
        var storms = new SystemTemporalStability();
        storms.StormData.nowStormActive = true;
        storms.StormData.stormDayNotify = 3;
        storms.StormData.nextStormTotalDays = 20;
        Assert.Equal(TemporalStormStage.Active, TemporalStormSource.Read(storms, 0));
        storms.StormData.stormDayNotify = -1;
        storms.StormData.stormGlitchStrength = .01f;
        Assert.Equal(TemporalStormStage.Active, TemporalStormSource.Read(storms, 0));
        Assert.Null(TemporalStormSource.Read(storms, double.NaN));
        Assert.Null(TemporalStormSource.Read(storms, double.PositiveInfinity));
        storms.StormData.nowStormActive = false;
        storms.StormData.nextStormTotalDays = double.NaN;
        Assert.Null(TemporalStormSource.Read(storms, 0));
        storms.StormData.nextStormTotalDays = double.PositiveInfinity;
        Assert.Null(TemporalStormSource.Read(storms, 0));
        Assert.Null(TemporalStormSource.Read(null, 0));
    }

    /// <summary>Native stage transitions preserve applicability and canonical measurements through configuration and session changes.</summary>
    [Fact]
    public void StageTransitionsVisibilityAndCleanup()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var storms = new SystemTemporalStability();
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.GetModSystem<SystemTemporalStability>(true)).Returns(storms);
        fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var calendar = new Mock<IClientGameCalendar>();
        calendar.SetupGet(value => value.TotalDays).Returns(0);
        fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
        var config = new TreeAttribute();
        config.SetBool("temporalStability", true);
        config.SetString("temporalStorms", "normal");
        fixture.World.SetupGet(value => value.Config).Returns(config);
        using var overlay = new TemporalStormOverlay(new TemporalStormPresentation(loadBackdrop: _ => null));
        overlay.BeginSession(fixture.Api.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        var bounds = ElementBounds.Fixed(10, 20, 24, 24).WithEmptyParent();
        bounds.CalcWorldBounds();
        Assert.True(overlay.IsApplicable());
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(SizeF.Empty, overlay.Measure());

        // Polling follows the native schedule; rendering reads only the already prepared snapshot.
        for (int index = 0; index < 3; index++)
        {
            storms.StormData.nextStormTotalDays = index == 0 ? .2 : .01;
            storms.StormData.nowStormActive = index == 2;
            Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
            overlay.Prepare(preparation);
            Assert.NotEqual(SizeF.Empty, overlay.Measure());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        }
        storms.StormData.nowStormActive = false;
        storms.StormData.nextStormTotalDays = 5;
        overlay.Refresh();
        Assert.True(overlay.IsApplicable());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        storms.StormData.nowStormActive = true;
        overlay.Refresh(); overlay.Prepare(preparation);
        config.SetString("temporalStorms", "off");
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        config.SetString("temporalStorms", "normal"); config.SetBool("temporalStability", false);
        Assert.False(overlay.IsApplicable());
        config.SetBool("temporalStability", true);
        overlay.Refresh(); overlay.Prepare(preparation);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.SetEnabled(true); overlay.Refresh(); overlay.Prepare(preparation);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        fixture.Entity.Alive = true; overlay.Refresh(); overlay.Prepare(preparation);
        overlay.EndSession();
        Assert.False(overlay.IsApplicable()); Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.Dispose();

    }

    /// <summary>The registered fixed slot precedes time and live configuration disables it immediately.</summary>
    [Fact]
    public void RegistrationReservesSlotAndUsesLiveToggles()
    {
        Assert.True(new VanillaExpandedConfig().EnableTemporalStormOverlay);
        var config = VanillaExpandedModSystem.Config;
        bool oldGlobal = config.EnableHudOverlays, oldFeature = config.EnableTemporalStormOverlay;
        var hud = new HudOverlaySystem();
        var system = new TemporalStormSystem();
        try
        {
            config.EnableHudOverlays = config.EnableTemporalStormOverlay = true;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object); system.StartClientSide(fixture.Api.Object);
            hud.Registry.RunPass(registration =>
            {
                Assert.Equal(TemporalStormSystem.OverlayId, registration.Id);
                Assert.Equal("player-status", registration.GroupId);
                Assert.Null(registration.ReservedSize);
                Assert.Equal(95, registration.Order);
                Assert.Equal("vanillaexpanded:hud-label-temporal-storm", registration.HoverLabelKey);
                Assert.True(registration.IsEnabled());
                config.EnableTemporalStormOverlay = false;
                system.OnConfigReloaded(fixture.Api.Object); Assert.False(registration.IsEnabled());
                config.EnableTemporalStormOverlay = true; config.EnableHudOverlays = false;
                system.OnConfigReloaded(fixture.Api.Object); Assert.False(registration.IsEnabled());
            });
        }
        finally { system.Dispose(); hud.Dispose(); config.EnableHudOverlays = oldGlobal; config.EnableTemporalStormOverlay = oldFeature; }
    }

    #endregion
}
