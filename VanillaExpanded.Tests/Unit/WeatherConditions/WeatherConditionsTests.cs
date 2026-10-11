using System.Drawing;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Moq;
using VanillaExpanded.WeatherConditions;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WeatherConditions;

/// <summary>Checks native condition classification and borrowed icon lifetime without rendering or loading assets.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WeatherConditionsTests
{
    #region Public API
    #region Classification
    /// <summary>Automatic snow follows the native snapshot threshold and explicit precipitation overrides climate.</summary>
    [Theory]
    [InlineData(EnumPrecipitationType.Auto, 3, EnumPrecipitationType.Snow)]
    [InlineData(EnumPrecipitationType.Auto, 4, EnumPrecipitationType.Rain)]
    [InlineData(EnumPrecipitationType.Auto, 20, EnumPrecipitationType.Rain)]
    [InlineData(EnumPrecipitationType.Snow, 20, EnumPrecipitationType.Snow)]
    [InlineData(EnumPrecipitationType.Rain, -20, EnumPrecipitationType.Rain)]
    [InlineData(EnumPrecipitationType.Hail, 20, EnumPrecipitationType.Hail)]
    public void NativePrecipitationSelection(EnumPrecipitationType type, float temperature, EnumPrecipitationType expected)
    {
        var sample = WeatherConditionsSample.FromWeather(.5f, type, temperature, 4, .005f);
        Assert.Equal(expected, sample.Precipitation);
        Assert.True(sample.Fog);
    }
    /// <summary>Fair weather, invalid measurements, and ordinary distance haze produce no icon.</summary>
    [Theory]
    [InlineData(0, .001f)]
    [InlineData(float.NaN, float.NaN)]
    [InlineData(float.PositiveInfinity, float.PositiveInfinity)]
    [InlineData(-1, -1)]
    public void FairOrInvalidWeatherIsHidden(float rain, float fog)
    {
        var sample = WeatherConditionsSample.FromWeather(rain, EnumPrecipitationType.Rain, 20, 4, fog);
        Assert.Null(sample.Precipitation);
        Assert.False(sample.Fog);
    }
    /// <summary>Blended transitions retain active icons until their lower exit thresholds are crossed.</summary>
    [Fact]
    public void IndependentHysteresis()
    {
        var active = WeatherConditionsSample.FromWeather(.02f, EnumPrecipitationType.Rain, 20, 4, .003f);
        var retained = WeatherConditionsSample.FromWeather(.007f, EnumPrecipitationType.Rain, 20, 4, .0022f, active);
        Assert.Equal(active, retained);
        var inactive = WeatherConditionsSample.FromWeather(.004f, EnumPrecipitationType.Rain, 20, 4, .0019f, retained);
        Assert.Null(inactive.Precipitation);
        Assert.False(inactive.Fog);
        var fresh = WeatherConditionsSample.FromWeather(.007f, EnumPrecipitationType.Rain, 20, 4, .0022f);
        Assert.Equal(inactive, fresh);
    }
    /// <summary>Unavailable automatic snow inputs never silently select rain.</summary>
    [Fact]
    public void InvalidAutoTemperatureDoesNotInventPrecipitation()
    {
        var sample = WeatherConditionsSample.FromWeather(.5f, EnumPrecipitationType.Auto, float.NaN, 4, .005f);
        Assert.Null(sample.Precipitation);
        Assert.True(sample.Fog);
    }
    #endregion
    #region Presentation lifetime
    /// <summary>Condition changes and GUI scaling reuse cached wrappers; resetting never disposes engine-owned textures.</summary>
    [Fact]
    public void CachedBorrowedTexturesAndMeasurement()
    {
        var api = new Mock<ICoreClientAPI>().Object;
        int loads = 0;
        var textures = Enumerable.Range(1, 4).Select(id => new LoadedTexture(api) { TextureId = id, Width = 96, Height = 96, IgnoreUndisposed = true }).ToArray();
        using var presentation = new WeatherConditionsPresentation(_ => { loads++; return textures; });
        var preparation = new HudOverlayPreparationContext(api, 1, "en");
        presentation.SetContent(new(EnumPrecipitationType.Rain, true));
        presentation.Prepare(preparation);
        Assert.Equal(new SizeF(51, 24), presentation.Size);
        presentation.SetContent(new(EnumPrecipitationType.Snow, false));
        presentation.Prepare(new HudOverlayPreparationContext(api, 2, "en"));
        Assert.Equal(new SizeF(24, 24), presentation.Size);
        Assert.Equal(1, loads);
        presentation.SetContent(new(null, true));
        presentation.Prepare(preparation);
        Assert.Equal(new SizeF(24, 24), presentation.Size);
        presentation.SetContent(new(null, false));
        presentation.Prepare(preparation);
        Assert.Equal(SizeF.Empty, presentation.Size);
        presentation.Reset();
        Assert.All(textures, texture => Assert.False(texture.Disposed));
        presentation.SetContent(new(EnumPrecipitationType.Hail, false));
        presentation.Prepare(preparation);
        Assert.Equal(2, loads);
        presentation.Prepare(new HudOverlayPreparationContext(new Mock<ICoreClientAPI>().Object, 1, "en"));
        Assert.Equal(3, loads);
    }
    /// <summary>Native sampling caches stable conditions and clears them for missing data, death, disable, and world exit.</summary>
    [Fact]
    public void SamplesNativeWeatherAndClearsStaleConditions()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var loader = new Mock<IModLoader>();
        var weather = new WeatherSystemClient();
        var snapshot = new WeatherDataSnapshot { BlendedPrecType = EnumPrecipitationType.Auto, snowThresholdTemp = 4 };
        snapshot.Ambient.FogDensity.Set(.005f, 1);
        // Seed the native provider's cached frame directly, avoiding startup simulation and graphics services.
        typeof(WeatherSystemClient).GetField("capi", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(weather, fixture.Api.Object);
        typeof(WeatherSystemClient).GetField("blendedWeatherDataCached", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(weather, snapshot);
        typeof(WeatherSystemClient).GetField("blendedLastCheckedMSDiv60", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(weather, 0L);
        loader.Setup(value => value.GetModSystem<WeatherSystemClient>(true)).Returns(weather);
        fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var blocks = new Mock<IBlockAccessor>();
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        ClimateCondition? climate = new() { Rainfall = .5f, Temperature = 2 };
        blocks.Setup(value => value.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.NowValues, 0)).Returns(() => climate!);
        int loads = 0;
        using var overlay = new WeatherConditionsOverlay(context =>
        {
            loads++;
            return Enumerable.Range(1, 4).Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray();
        });
        overlay.BeginSession(fixture.Api.Object);
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Equal(EnumPrecipitationType.Snow, overlay.Sample!.Precipitation);
        Assert.True(overlay.Sample.Fog);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.Prepare(preparation);
        Assert.Equal(new SizeF(51, 24), overlay.Measure());
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        // Native changes after sampling cannot alter the icon until the next refresh.
        climate.Temperature = 20;
        var renderer = new Mock<IRenderAPI>();
        var bounds = ElementBounds.Fixed(0, 0, 51, 24).WithEmptyParent();
        bounds.CalcWorldBounds();
        overlay.Draw(renderer.Object, bounds, new RectangleF(0, 0, 51, 24), .1f);
        Assert.Equal(new[] { 2, 4 }, renderer.Invocations.Where(call => call.Method.Name == "RenderTexture").Select(call => (int)call.Arguments[0]).ToArray());
        overlay.Refresh();
        Assert.Equal(EnumPrecipitationType.Rain, overlay.Sample!.Precipitation);
        overlay.Prepare(preparation);
        Assert.Equal(1, loads);
        climate = null;
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Null(overlay.Sample);
        Assert.Equal(SizeF.Empty, overlay.Measure());
        climate = new() { Rainfall = .5f, Temperature = 2 };
        overlay.Refresh();
        overlay.SetEnabled(false);
        Assert.Null(overlay.Sample);
        overlay.SetEnabled(true);
        overlay.Refresh();
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
        Assert.Null(overlay.Sample);
        fixture.Entity.Alive = true;
        overlay.Refresh();
        overlay.EndSession();
        Assert.Null(overlay.Sample);
        Assert.Equal(SizeF.Empty, overlay.Measure());
    }

    /// <summary>Disabled and unbound overlays cannot expose stale weather or allocate textures.</summary>
    [Fact]
    public void InactiveOverlayHasNoPresentation()
    {
        using var overlay = new WeatherConditionsOverlay(_ => throw new InvalidOperationException("Inactive artwork was loaded."));
        Assert.False(overlay.IsApplicable());
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        overlay.SetEnabled(false);
        Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.EndSession();
        overlay.EndSession();
        Assert.Null(overlay.Sample);
    }
    #endregion
    #endregion
}
