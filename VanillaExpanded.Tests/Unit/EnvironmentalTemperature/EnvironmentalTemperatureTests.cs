using System.Drawing;
using Moq;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.EnvironmentalTemperature;

/// <summary>Checks current climate sampling, continuous scale position, reading cache, and presentation lifetime.</summary>
[Collection("HudOverlayGeometry")]
public sealed class EnvironmentalTemperatureTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    #region Fixture lifetime
    /// <summary>Provides deterministic Celsius text without a native language loader.</summary>
    public EnvironmentalTemperatureTests()
    {
        var service = new Mock<ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] arguments) => arguments.Length == 1 ? $"{arguments[0]}°C" : key);
        Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores the borrowed translation registry after each test.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }
    #endregion

    #region Sampling
    /// <summary>Local current climate includes freezing values and zero, while nonfinite data is unavailable.</summary>
    [Theory]
    [InlineData(-12f, -12f)] [InlineData(0f, 0f)] [InlineData(32f, 32f)]
    [InlineData(float.NaN, null)] [InlineData(float.PositiveInfinity, null)]
    public void SamplesLocalCurrentClimate(float raw, float? expected)
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var blocks = new Mock<IBlockAccessor>();
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        var climate = new ClimateCondition { Temperature = raw };
        blocks.Setup(value => value.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.NowValues, 0)).Returns(climate);
        using var overlay = new EnvironmentalTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        Assert.Equal(expected, overlay.Temperature);
        Assert.Equal(raw, climate.Temperature);
        blocks.Verify(value => value.GetClimateAt(It.Is<BlockPos>(pos => pos.Equals(fixture.Entity.Pos.AsBlockPos)), EnumGetClimateMode.NowValues, 0), Times.Once);
        overlay.SetEnabled(false);
        Assert.Null(overlay.Temperature);
        Assert.False(overlay.IsApplicable());
        overlay.SetEnabled(true);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
    }

    /// <summary>Missing climate clears a prior reading and world exit releases sampled state.</summary>
    [Fact]
    public void MissingClimateAndWorldExitClearReading()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var blocks = new Mock<IBlockAccessor>();
        fixture.World.SetupGet(value => value.BlockAccessor).Returns(blocks.Object);
        ClimateCondition? climate = new() { Temperature = 12 };
        blocks.Setup(value => value.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.NowValues, 0)).Returns(() => climate!);
        using var overlay = new EnvironmentalTemperatureOverlay();
        overlay.BeginSession(fixture.Api.Object);
        overlay.Refresh();
        Assert.Equal(12f, overlay.Temperature);
        climate = null;
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.Null(overlay.Temperature);
        climate = new() { Temperature = -8 };
        overlay.Refresh();
        overlay.EndSession();
        Assert.Null(overlay.Temperature);
        Assert.Equal(SizeF.Empty, overlay.Measure());
    }
    #endregion

    #region Presentation



    #endregion
    #endregion

}
