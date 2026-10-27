using System.Drawing;
using Moq;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
namespace VanillaExpanded.Tests.Unit.BodyTemperature;
/// <summary>Checks raw temperature geometry, hysteresis, and reusable native thermometer layers.</summary>
[Collection("HudOverlayGeometry")]
public sealed class BodyTemperatureThermometerTests : IDisposable
{
    private readonly Vintagestory.API.Config.ITranslationService? english = Vintagestory.API.Config.Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    /// <summary>Installs a deterministic reading label for headless geometry checks.</summary>
    public BodyTemperatureThermometerTests()
    {
        var service = new Mock<Vintagestory.API.Config.ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("34°C");
        Vintagestory.API.Config.Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores the borrowed global language service after each test.</summary>
    public void Dispose()
    {
        if (english == null) Vintagestory.API.Config.Lang.AvailableLanguages.Remove("en");
        else Vintagestory.API.Config.Lang.AvailableLanguages["en"] = english;
    }
    #region Classification
    /// <summary>Reproduces the character panel conversion without changing the sampled simulation value.</summary>
    [Theory]
    [InlineData(32, 32)]
    [InlineData(36.5f, 37)]
    [InlineData(37, 37)]
    [InlineData(41, 37)]
    [InlineData(42, 38)]
    [InlineData(45, 38)]
    public void ReadingUsesCharacterPanelConversion(float raw, int expected)
    {
        var sample = new BodyTemperatureSample(raw, 37, BodyTemperatureRisk.Hot);
        Assert.Equal(expected, sample.DisplayCelsius);
        Assert.Equal(raw, sample.Celsius);
    }

    /// <summary>Checks converted hot bands and rejects the normal native warmth ceiling.</summary>
    [Theory]
    [InlineData(41, null)]
    [InlineData(42, null)]
    [InlineData(45, null)]
    [InlineData(87, null)]
    [InlineData(87.1f, 2)]
    [InlineData(107, 2)]
    [InlineData(107.1f, 3)]
    public void HotBandsUseRawThresholds(float current, int? expected)
        => Assert.Equal(expected, (int?)BodyTemperatureSample.FromTemperature(current, 37, 0)?.Risk);
    /// <summary>Native cold clears immediately while visual hot recovery retains its original margin.</summary>
    [Fact]
    public void NativeColdClearsAndHotRetainsRecoveryHysteresis()
    {
        var cold = BodyTemperatureSample.FromTemperature(34, 37, .25f)!;
        Assert.Null(BodyTemperatureSample.FromTemperature(34, 37, 0, cold));
        Assert.NotNull(BodyTemperatureSample.FromTemperature(37, 37, .25f, cold));
        var hot = BodyTemperatureSample.FromTemperature(97, 37, 0)!;
        Assert.NotNull(BodyTemperatureSample.FromTemperature(85, 37, 0, hot));
        Assert.Null(BodyTemperatureSample.FromTemperature(84.5f, 37, 0, hot));
        Assert.Null(BodyTemperatureSample.FromTemperature(35, 37, 0, hot));
    }
    /// <summary>Fill follows unrounded temperature while text rounds whole degrees away from zero.</summary>
    [Fact]
    public void FillAndTextHaveIndependentPrecision()
    {
        var sample = new BodyTemperatureSample(34.5f, 37, BodyTemperatureRisk.Cold);
        Assert.Equal(35, sample.DisplayCelsius);
        Assert.Equal(.25f, sample.Fill);
        Assert.Equal(0, (sample with { Celsius = 20 }).Fill);
        Assert.Equal(1, (sample with { Celsius = 117 }).Fill);
        Assert.NotEqual(sample.Fill, (sample with { Celsius = 34.51f }).Fill);
    }
    #endregion
    #endregion
}
