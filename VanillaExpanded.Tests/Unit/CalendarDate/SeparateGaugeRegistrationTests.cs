using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.CalendarDate;

/// <summary>Checks that all three instruments remain independently configurable in the environmental stack.</summary>
[Collection("HudOverlayGeometry")]
public sealed class SeparateGaugeRegistrationTests
{
    #region Public API
    /// <summary>Clock visibility never suppresses temperature or calendar, and each reserves no hidden space.</summary>
    [Fact]
    public void IndependentTogglesAndStackOrder()
    {
        var config = VanillaExpandedModSystem.Config;
        bool global = config.EnableHudOverlays, time = config.EnableApproximateTimeOverlay,
            temperature = config.EnableEnvironmentalTemperatureOverlay, date = config.EnableCalendarDateOverlay;
        var api = new Mock<ICoreClientAPI>(); var events = new Mock<IClientEventAPI>(); var loader = new Mock<IModLoader>();
        api.SetupGet(value => value.Event).Returns(events.Object); api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var hud = new HudOverlaySystem();
        var clock = new ApproximateTimeSystem();
        var thermometer = new EnvironmentalTemperatureSystem();
        var calendar = new CalendarDateSystem();
        loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
        try
        {
            config.EnableHudOverlays = config.EnableApproximateTimeOverlay = config.EnableEnvironmentalTemperatureOverlay = config.EnableCalendarDateOverlay = true;
            hud.StartClientSide(api.Object); clock.StartClientSide(api.Object); thermometer.StartClientSide(api.Object); calendar.StartClientSide(api.Object);
            var registrations = new List<HudOverlayRegistration>(); hud.Registry.RunPass(registrations.Add);
            Assert.Equal(new[] { ApproximateTimeSystem.OverlayId, EnvironmentalTemperatureSystem.OverlayId, CalendarDateSystem.OverlayId }, registrations.OrderBy(value => value.Order).Select(value => value.Id));
            Assert.All(registrations, value => { Assert.True(value.IsEnabled()); Assert.Null(value.ReservedSize); Assert.Equal(300, value.RefreshIntervalMs); Assert.Equal("world-status", value.GroupId); });
            config.EnableApproximateTimeOverlay = false; clock.OnConfigReloaded(api.Object);
            Assert.False(registrations.Single(value => value.Id == ApproximateTimeSystem.OverlayId).IsEnabled());
            Assert.True(registrations.Single(value => value.Id == EnvironmentalTemperatureSystem.OverlayId).IsEnabled());
            Assert.True(registrations.Single(value => value.Id == CalendarDateSystem.OverlayId).IsEnabled());
            config.EnableCalendarDateOverlay = false; calendar.OnConfigReloaded(api.Object);
            Assert.False(registrations.Single(value => value.Id == CalendarDateSystem.OverlayId).IsEnabled());
            config.EnableHudOverlays = false;
            Assert.All(registrations, value => Assert.False(value.IsEnabled()));
        }
        finally
        {
            calendar.Dispose(); thermometer.Dispose(); clock.Dispose(); hud.Dispose();
            config.EnableHudOverlays = global; config.EnableApproximateTimeOverlay = time;
            config.EnableEnvironmentalTemperatureOverlay = temperature; config.EnableCalendarDateOverlay = date;
        }
    }
    #endregion
}
