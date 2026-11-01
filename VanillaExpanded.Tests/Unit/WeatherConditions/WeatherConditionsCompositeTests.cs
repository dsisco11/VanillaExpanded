using VanillaExpanded.WeatherConditions;
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
    [InlineData(EnumPrecipitationType.Rain, 2)]
    [InlineData(EnumPrecipitationType.Snow, 5)]
    [InlineData(EnumPrecipitationType.Hail, 8)]
    public void CrowdedWeatherRetainsEveryChannel(EnumPrecipitationType type, int precipitationLayer)
    {
        var sample = WeatherConditionsSample.FromWeather(1, type, 20, 4, .05f,
            thunderRate: 1);
        Assert.Equal(3, sample.PrecipitationIntensity);
        Assert.Equal(3, sample.FogIntensity);
        Assert.Equal(3, sample.ThunderIntensity);
        Assert.Equal(new[] { precipitationLayer, 11, 14 }, WeatherConditionsLayers.Select(sample));
        Assert.DoesNotContain(WeatherConditionsLayers.Names, name => name.StartsWith("sky-"));
    }

    /// <summary>Each intensity retains its own band inside the exit margin and falls independently below it.</summary>
    [Fact]
    public void BandsHaveIndependentExitMargins()
    {
        var active = WeatherConditionsSample.FromWeather(.7f, EnumPrecipitationType.Rain, 20, 4, .03f,
            thunderRate: .8f);
        var retained = WeatherConditionsSample.FromWeather(.6f, EnumPrecipitationType.Rain, 20, 4, .018f, active,
            thunderRate: .6f);
        Assert.Equal(active, retained);
        var changed = WeatherConditionsSample.FromWeather(.54f, EnumPrecipitationType.Rain, 20, 4, .018f, retained,
            thunderRate: .6f);
        Assert.Equal(2, changed.PrecipitationIntensity);
        Assert.Equal(active with { PrecipitationIntensity = 2 }, changed);
    }

    /// <summary>Dry fog remains visible without precipitation; clear and invalid data select no layers.</summary>
    [Fact]
    public void IndependentDryConditionsAndInvalidWeather()
    {
        var clear = WeatherConditionsSample.FromWeather(0, EnumPrecipitationType.Rain, 20, 4, .001f);
        Assert.Empty(WeatherConditionsLayers.Select(clear));
        Assert.Empty(WeatherConditionsText.Format(clear));
        var dry = WeatherConditionsSample.FromWeather(0, EnumPrecipitationType.Rain, 20, 4, .001f,
            mistOpacity: .9f);
        Assert.Null(dry.Precipitation);
        Assert.Equal(new[] { 11 }, WeatherConditionsLayers.Select(dry));
        var invalid = WeatherConditionsSample.FromWeather(float.NaN, EnumPrecipitationType.Auto, float.NaN, 4,
            float.PositiveInfinity, thunderRate: float.PositiveInfinity);
        Assert.Empty(WeatherConditionsLayers.Select(invalid));
        Assert.Empty(WeatherConditionsText.Format(invalid));
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
    }

    /// <summary>Precipitation retains every intensity band without a cloud channel.</summary>
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(.1f, 1)]
    [InlineData(.4f, 2)]
    [InlineData(.8f, 3)]
    [InlineData(1f, 3)]
    public void PrecipitationIntensityBands(float value, int precipitation)
    {
        var sample = WeatherConditionsSample.FromWeather(value, EnumPrecipitationType.Rain, 20, 4, 0);
        Assert.Equal(precipitation, sample.PrecipitationIntensity);
    }
    #endregion

    #endregion
}
