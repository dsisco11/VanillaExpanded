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

    /// <summary>Native stage transitions select textures and queue backgrounds separately from foreground draws.</summary>
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
        var renderer = new Mock<IRenderAPI>();
        var backdrop = new Mock<IHudOverlayIconBackdrop>();
        int loads = 0;
        var borrowed = new List<LoadedTexture>();
        using var overlay = new TemporalStormOverlay(context =>
        {
            loads++;
            var textures = Enumerable.Range(1, 3).Select(id => new LoadedTexture(context.Api)
                { TextureId = id, IgnoreUndisposed = true }).ToArray();
            borrowed.AddRange(textures);
            return textures;
        }, _ => backdrop.Object);
        overlay.BeginSession(fixture.Api.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        var bounds = ElementBounds.Fixed(10, 20, 24, 24).WithEmptyParent();
        bounds.CalcWorldBounds();
        Assert.True(overlay.IsApplicable());
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(SizeF.Empty, overlay.Measure()); Assert.Equal(0, loads);

        // Polling follows the native schedule; rendering reads only the already prepared snapshot.
        for (int index = 0; index < 3; index++)
        {
            storms.StormData.nextStormTotalDays = index == 0 ? .2 : .01;
            storms.StormData.nowStormActive = index == 2;
            Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
            overlay.Prepare(preparation);
            Assert.Equal(new SizeF(24, 24), overlay.Measure());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh());
            renderer.Invocations.Clear(); backdrop.Invocations.Clear();
            overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 0);
            Assert.Empty(backdrop.Invocations);
            Assert.Equal(index + 1, Assert.Single(renderer.Invocations).Arguments[0]);
            overlay.CollectBackdrops(renderer.Object, bounds);
            Assert.Equal("Queue", Assert.Single(backdrop.Invocations).Method.Name);
        }
        Assert.Equal(1, loads);
        storms.StormData.nowStormActive = false;
        storms.StormData.nextStormTotalDays = 5;
        overlay.Refresh();
        Assert.True(overlay.IsApplicable());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        renderer.Invocations.Clear(); backdrop.Invocations.Clear();
        overlay.CollectBackdrops(renderer.Object, bounds);
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 0);
        Assert.Empty(renderer.Invocations); Assert.Empty(backdrop.Invocations);

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
        Assert.All(borrowed, texture => Assert.True(texture.TextureId > 0));
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

    /// <summary>Imminent opacity responds to urgency-only refreshes while other stages remain steady.</summary>
    [Fact]
    public void ImminentPulseTracksUrgencyAndResetsWithSession()
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
        config.SetBool("temporalStability", true); config.SetString("temporalStorms", "normal");
        fixture.World.SetupGet(value => value.Config).Returns(config);
        var renderer = new Mock<IRenderAPI>();
        using var overlay = new TemporalStormOverlay(context => Enumerable.Range(1, 3)
            .Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray(), _ => null);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        var bounds = ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(); bounds.CalcWorldBounds();
        overlay.BeginSession(fixture.Api.Object);
        storms.StormData.nextStormTotalDays = .02;
        overlay.Refresh(); overlay.Prepare(preparation);
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, .5f);
        float firstAlpha = Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W;
        Assert.Equal(.675f, firstAlpha, 5);
        storms.StormData.nextStormTotalDays = 0;
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 1f / 12);
        Assert.Equal(.35f, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);

        // World exit starts a fresh warning; approaching and active artwork carry no animation tint.
        overlay.EndSession(); overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh(); overlay.Prepare(preparation); renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 0);
        Assert.Equal(1, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);
        storms.StormData.nextStormTotalDays = .2;
        overlay.Refresh(); overlay.Prepare(preparation); renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, .5f);
        Assert.Null(Assert.Single(renderer.Invocations).Arguments[6]);
        storms.StormData.nowStormActive = true;
        storms.StormData.stormActiveTotalDays = 1;
        overlay.Refresh(); overlay.Prepare(preparation); renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, .5f);
        Assert.Null(Assert.Single(renderer.Invocations).Arguments[6]);
    }

    /// <summary>Waning is inferred from the end deadline, pulses continuously, and remains active until the server clears it.</summary>
    [Fact]
    public void ActiveStormPulsesOnlyWithinWaningWindow()
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
        config.SetBool("temporalStability", true); config.SetString("temporalStorms", "normal");
        fixture.World.SetupGet(value => value.Config).Returns(config);
        var renderer = new Mock<IRenderAPI>();
        var backdrop = new Mock<IHudOverlayIconBackdrop>();
        using var overlay = new TemporalStormOverlay(context => Enumerable.Range(1, 3)
            .Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray(), _ => backdrop.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        var bounds = ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(); bounds.CalcWorldBounds();
        overlay.BeginSession(fixture.Api.Object);
        storms.StormData.nowStormActive = true;
        storms.StormData.nextStormTotalDays = -1;
        storms.StormData.stormActiveTotalDays = .02;
        storms.StormData.stormDayNotify = -1;
        overlay.Refresh(); overlay.Prepare(preparation);
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 1);
        Assert.Null(Assert.Single(renderer.Invocations).Arguments[6]);

        // The exact native boundary remains steady even with a stale waning notification marker.
        storms.StormData.stormDayNotify = 99;
        storms.StormData.stormActiveTotalDays = .0199;
        Assert.Equal(HudOverlayChange.None, overlay.Refresh()); renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 0);
        Assert.Equal(1, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);
        renderer.Invocations.Clear();
        double nearWindowRate = .5 + 2.5 * (1 - .0199 / .02);
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, (float)(.25 / nearWindowRate));
        Assert.Equal(.675f, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);

        // Deadline updates preserve phase and accelerate the existing pulse to three cycles per second.
        storms.StormData.stormActiveTotalDays = 0;
        Assert.Equal(HudOverlayChange.None, overlay.Refresh()); renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 0);
        Assert.Equal(.675f, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 1f / 12);
        Assert.Equal(.35f, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);
        storms.StormData.stormActiveTotalDays = -1;
        Assert.Equal(HudOverlayChange.None, overlay.Refresh()); renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, 1f / 6);
        Assert.Equal(1, Assert.IsType<Vintagestory.API.MathTools.Vec4f>(Assert.Single(renderer.Invocations).Arguments[6]).W, 5);
        Assert.Equal(new SizeF(24, 24), overlay.Measure());

        storms.StormData.nowStormActive = false; storms.StormData.nextStormTotalDays = 5;
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        renderer.Invocations.Clear(); backdrop.Invocations.Clear();
        overlay.Draw(renderer.Object, bounds, RectangleF.Empty, .5f);
        overlay.CollectBackdrops(renderer.Object, bounds);
        Assert.Empty(renderer.Invocations); Assert.Empty(backdrop.Invocations);
    }
    #endregion
}


