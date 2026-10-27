using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WeatherConditions;

/// <summary>Verifies independent weather bands, native gates, and full-canvas composition without creating artwork.</summary>
[Collection("HudOverlayGeometry")]
public sealed class WeatherConditionsCompositeTests
{
    #region Public API
    #region Classification
    /// <summary>All channels can coexist at maximum intensity without overwriting one another.</summary>
    [Theory]
    [InlineData(EnumPrecipitationType.Rain, 8)]
    [InlineData(EnumPrecipitationType.Snow, 11)]
    [InlineData(EnumPrecipitationType.Hail, 14)]
    public void CrowdedWeatherRetainsEveryChannel(EnumPrecipitationType type, int precipitationLayer)
    {
        var sample = WeatherConditionsSample.FromWeather(1, type, 20, 4, .05f,
            cloudCoverage: 1, thunderRate: 1);
        Assert.Equal(3, sample.PrecipitationIntensity);
        Assert.Equal(4, sample.CloudCoverage);
        Assert.Equal(3, sample.FogIntensity);
        Assert.Equal(3, sample.ThunderIntensity);
        Assert.Equal(new[] { 4, precipitationLayer, 17, 20 }, WeatherConditionsLayers.Select(sample));
    }

    /// <summary>Each intensity retains its own band inside the exit margin and falls independently below it.</summary>
    [Fact]
    public void BandsHaveIndependentExitMargins()
    {
        var active = WeatherConditionsSample.FromWeather(.7f, EnumPrecipitationType.Rain, 20, 4, .03f,
            cloudCoverage: .95f, thunderRate: .8f);
        var retained = WeatherConditionsSample.FromWeather(.6f, EnumPrecipitationType.Rain, 20, 4, .018f, active,
            cloudCoverage: .88f, thunderRate: .6f);
        Assert.Equal(active, retained);
        var changed = WeatherConditionsSample.FromWeather(.54f, EnumPrecipitationType.Rain, 20, 4, .018f, retained,
            cloudCoverage: .88f, thunderRate: .6f);
        Assert.Equal(2, changed.PrecipitationIntensity);
        Assert.Equal(active with { PrecipitationIntensity = 2 }, changed);
    }

    /// <summary>Clear, dry fog, and unknown cloud data remain distinct states.</summary>
    [Fact]
    public void IndependentDryConditionsAndUnknownSky()
    {
        var clear = WeatherConditionsSample.FromWeather(0, EnumPrecipitationType.Rain, 20, 4, .001f, cloudCoverage: 0);
        Assert.Equal(new[] { 0 }, WeatherConditionsLayers.Select(clear));
        var dry = WeatherConditionsSample.FromWeather(0, EnumPrecipitationType.Rain, 20, 4, .001f,
            cloudCoverage: 0, mistOpacity: .9f);
        Assert.Null(dry.Precipitation);
        Assert.Equal(new[] { 0, 17 }, WeatherConditionsLayers.Select(dry));
        var invalid = WeatherConditionsSample.FromWeather(float.NaN, EnumPrecipitationType.Auto, float.NaN, 4,
            float.PositiveInfinity, cloudCoverage: float.NaN, thunderRate: float.PositiveInfinity);
        Assert.Null(invalid.CloudCoverage);
        Assert.Equal(new[] { 5 }, WeatherConditionsLayers.Select(invalid));
        Assert.False(invalid.Fog);
    }

    /// <summary>Thunder depends on native temperature and rain-cloud eligibility, independently of precipitation type.</summary>
    [Theory]
    [InlineData(11, 1, .3f, .4f, 0)]
    [InlineData(12, 0, .3f, .4f, 0)]
    [InlineData(12, .5f, .3f, .4f, .35f)]
    [InlineData(20, 1, .8f, .8f, 1)]
    [InlineData(float.NaN, 1, .3f, .4f, 0)]
    public void ThunderUsesNativeGates(float temperature, float clouds, float near, float distant, float expected)
    {
        Assert.Equal(expected, WeatherConditionsSampler.ThunderRate(temperature, 12, clouds, near, distant), 5);
    }

    /// <summary>Ground mist affects players below its layer and never becomes dense fog merely through absolute density.</summary>
    [Fact]
    public void MistRespectsSignedDensityAndAltitude()
    {
        Assert.True(WeatherConditionsSampler.LocalMistOpacity(-.1f, 110, 100) > .6f);
        Assert.Equal(0, WeatherConditionsSampler.LocalMistOpacity(-.1f, 110, 120));
        Assert.Equal(0, WeatherConditionsSampler.LocalMistOpacity(-.1f, 110, 110));
        Assert.Equal(0, WeatherConditionsSampler.LocalMistOpacity(float.NaN, 110, 100));
        Assert.True(float.IsNaN(WeatherConditionsSampler.EstimateCloudCoverage(new WeatherSystemClient(), new())));
    }

    /// <summary>Cloud sampling uses visible tile alpha rather than vertical thickness and does not regenerate native caches.</summary>
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(.05, 1, .5)]
    [InlineData(.2, 1, 1)]
    [InlineData(.2, .25, .25)]
    public void CloudCoverageReadsExistingTileAlpha(double thickness, float opacity, float expected)
    {
        var weather = new WeatherSystemClient { CloudTileSize = 32 };
        var pattern = new WeatherPattern(weather, new WeatherPatternConfig(), new(), 0, 0);
        pattern.State.nowbaseThickness = (float)thickness;
        pattern.State.nowThicknessMul = 1;
        pattern.State.nowbaseOpaqueness = opacity;
        var cache = new double[9, 9];
        pattern.CloudDensityNoiseCache = cache;
        typeof(WeatherPattern).GetField("tilesPerRegion", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pattern, 9);
        var region = (WeatherSimulationRegion)RuntimeHelpers.GetUninitializedObject(typeof(WeatherSimulationRegion));
        region.NewWePattern = region.OldWePattern = pattern;
        region.Weight = 1;
        var reader = (WeatherDataReaderPreLoad)RuntimeHelpers.GetUninitializedObject(typeof(WeatherDataReaderPreLoad));
        reader.AdjacentSims = [region, region, region, region];
        // Reproduce startup: the public patterns exist, but the reader has not bound its private rain overlay yet.
        Assert.Throws<NullReferenceException>(() => reader.GetBlendedCloudOpaqueness());
        weather.WeatherDataAtPlayer = reader;
        weather.rainOverlayPattern = pattern;
        Assert.Equal(expected, WeatherConditionsSampler.EstimateCloudCoverage(weather, new()), 5);
        Assert.Same(cache, pattern.CloudDensityNoiseCache);
        Assert.All(cache.Cast<double>(), value => Assert.Equal(0, value));
        pattern.CloudDensityNoiseCache = null!;
        Assert.True(float.IsNaN(WeatherConditionsSampler.EstimateCloudCoverage(weather, new())));
    }

    /// <summary>The same normalized input can independently select every cloud and intensity band.</summary>
    [Theory]
    [InlineData(0f, 0, 0)]
    [InlineData(.1f, 1, 1)]
    [InlineData(.4f, 2, 2)]
    [InlineData(.8f, 3, 3)]
    [InlineData(1f, 4, 3)]
    public void CoverageAndIntensityBands(float value, int cloud, int precipitation)
    {
        var sample = WeatherConditionsSample.FromWeather(value, EnumPrecipitationType.Rain, 20, 4, 0,
            cloudCoverage: value);
        Assert.Equal(cloud, sample.CloudCoverage);
        Assert.Equal(precipitation, sample.PrecipitationIntensity);
    }
    #endregion

    #endregion
}
