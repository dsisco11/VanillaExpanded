using VanillaExpanded.ApproximateTime;
using VanillaExpanded.CalendarDate;
using VanillaExpanded.EnvironmentalTemperature;
using System.Reflection;
using Moq;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.BowAmmunition;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.PlayerWetness;
using VanillaExpanded.RiftActivity;
using VanillaExpanded.TemporalStorm;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks live text-mode invalidation for unchanged survival, weather, and temporal state.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudIndicatorTextRefreshTests
{
    #region Public API
    /// <summary>Each existing compact indicator schedules both preparation and measurement after either style transition.</summary>
    [Theory]
    [InlineData("ammunition")] [InlineData("body-temperature")] [InlineData("wetness")]
    [InlineData("weather")] [InlineData("rift")] [InlineData("storm")]
    public void UnchangedSampleStillInvalidatesAfterStyleChange(string kind)
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); fixture.Entity.Alive = true;
        var loader = new Mock<IModLoader>(); fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var calendar = new Mock<IClientGameCalendar>(); fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
        IHudOverlay overlay;
        Action<HudIndicatorStyle> select;
        switch (kind)
        {
            case "ammunition":
                var ammunition = new BowAmmunitionOverlay(() => { }); overlay = ammunition; select = ammunition.SetStyle; break;
            case "wetness":
                var worldData = new Mock<IWorldPlayerData>(); worldData.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Survival);
                fixture.Player.SetupGet(value => value.WorldData).Returns(worldData.Object);
                fixture.Entity.WatchedAttributes.SetFloat("wetness", .4f);
                var wetness = new PlayerWetnessOverlay(); overlay = wetness; select = wetness.SetStyle; break;
            case "body-temperature":
                ConfigureBody(fixture);
                var body = new BodyTemperatureOverlay(); overlay = body; select = body.SetStyle; break;
            case "rift":
                var rifts = new ModSystemRiftWeather();
                typeof(ModSystemRiftWeather).GetField("curPattern", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(rifts, new CurrentPattern { Code = "high" });
                loader.Setup(value => value.GetModSystem<ModSystemRiftWeather>(true)).Returns(rifts);
                var rift = new RiftActivityOverlay(); overlay = rift; select = rift.SetStyle; break;
            case "storm":
                var storms = new SystemTemporalStability(); storms.StormData.nextStormTotalDays = .01;
                loader.Setup(value => value.GetModSystem<SystemTemporalStability>(true)).Returns(storms);
                var config = new TreeAttribute(); config.SetBool("temporalStability", true); config.SetString("temporalStorms", "normal");
                fixture.World.SetupGet(value => value.Config).Returns(config);
                var storm = new TemporalStormOverlay(); overlay = storm; select = storm.SetStyle; break;
            case "weather":
                ConfigureWeather(fixture, loader);
                var weather = new WeatherConditionsOverlay(); overlay = weather; select = weather.SetStyle; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        using (overlay)
        {
            overlay.BeginSession(fixture.Api.Object);
            Assert.True(overlay.IsApplicable());
            Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh());
            select(HudIndicatorStyle.Text);
            Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh());
            select(HudIndicatorStyle.Text);
            Assert.Equal(HudOverlayChange.None, overlay.Refresh());
            select(HudIndicatorStyle.Icon);
            Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
        }
    }
    /// <summary>All three environmental overlays invalidate measurement for text mode even without a changed calendar or climate sample.</summary>
    [Theory]
    [InlineData("time")] [InlineData("temperature")] [InlineData("date")]
    public void EnvironmentalTextStyleInvalidatesUnchangedReadings(string kind)
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); fixture.Entity.Alive = true;
        var calendar = new Mock<IClientGameCalendar>();
        calendar.SetupGet(value => value.TotalDays).Returns(.5);
        calendar.SetupGet(value => value.HoursPerDay).Returns(24);
        calendar.SetupGet(value => value.DaysPerMonth).Returns(9);
        calendar.SetupGet(value => value.MonthName).Returns(EnumMonth.October);
        calendar.Setup(value => value.GetSunPosition(It.IsAny<Vec3d>(), It.IsAny<double>())).Returns(new Vec3f(0, 1, 0));
        fixture.World.SetupGet(value => value.Calendar).Returns(calendar.Object);
        var blocks = new Mock<IBlockAccessor>();
        blocks.Setup(value => value.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.NowValues, 0)).Returns(new ClimateCondition { Temperature = 12 });
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        IHudOverlay overlay;
        Action<EnvironmentalGaugeStyle> select;
        switch (kind)
        {
            case "time": var time = new ApproximateTimeOverlay(); overlay = time; select = time.SetStyle; break;
            case "temperature": var temperature = new EnvironmentalTemperatureOverlay(); overlay = temperature; select = temperature.SetStyle; break;
            case "date": var date = new CalendarDateOverlay(); overlay = date; select = date.SetStyle; break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        using (overlay)
        {
            overlay.BeginSession(fixture.Api.Object);
            Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh() & HudOverlayChange.Measurement);
            select(EnvironmentalGaugeStyle.Text);
            Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh() & HudOverlayChange.Measurement);
            select(EnvironmentalGaugeStyle.Text);
            Assert.Equal(HudOverlayChange.None, overlay.Refresh() & HudOverlayChange.Measurement);
            select(EnvironmentalGaugeStyle.Immersive);
            Assert.Equal(HudOverlayChange.Presentation | HudOverlayChange.Measurement, overlay.Refresh());
        }
    }
    #endregion

    #region Private
    /// <summary>Seeds the native temperature behavior and synchronized cold warning without running server simulation.</summary>
    private static void ConfigureBody(BowAmmunitionSamplerTests.Fixture fixture)
    {
        fixture.Api.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        fixture.World.SetupGet(value => value.Side).Returns(EnumAppSide.Client);
        fixture.Entity.Api = fixture.Api.Object;
        typeof(Entity).GetProperty("Properties")!.SetValue(fixture.Entity,
            new EntityProperties { Client = new EntityClientProperties([], new Dictionary<string, JsonObject>()) });
        fixture.Entity.Properties.Client.Behaviors.Add(new EntityBehaviorBodyTemperature(fixture.Entity) { NormalBodyTemperature = 37 });
        var data = new Mock<IWorldPlayerData>(); data.SetupGet(value => value.CurrentGameMode).Returns(EnumGameMode.Survival);
        fixture.Player.SetupGet(value => value.WorldData).Returns(data.Object);
        var temperature = new TreeAttribute(); temperature.SetFloat("bodytemp", 33);
        fixture.Entity.WatchedAttributes.SetAttribute("bodyTemp", temperature);
        fixture.Entity.WatchedAttributes.SetFloat("freezingEffectStrength", .6f);
    }

    /// <summary>Seeds the native weather provider's cached frame so repeated samples remain identical.</summary>
    private static void ConfigureWeather(BowAmmunitionSamplerTests.Fixture fixture, Mock<IModLoader> loader)
    {
        var weather = new WeatherSystemClient();
        var snapshot = new WeatherDataSnapshot { BlendedPrecType = EnumPrecipitationType.Rain, snowThresholdTemp = 4 };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(WeatherSystemClient).GetField("capi", flags)!.SetValue(weather, fixture.Api.Object);
        typeof(WeatherSystemClient).GetField("blendedWeatherDataCached", flags)!.SetValue(weather, snapshot);
        typeof(WeatherSystemClient).GetField("blendedLastCheckedMSDiv60", flags)!.SetValue(weather, 0L);
        loader.Setup(value => value.GetModSystem<WeatherSystemClient>(true)).Returns(weather);
        var blocks = new Mock<IBlockAccessor>(); fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        blocks.Setup(value => value.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.NowValues, 0))
            .Returns(new ClimateCondition { Rainfall = .5f, Temperature = 10, RainCloudOverlay = 1 });
    }
    #endregion
}

