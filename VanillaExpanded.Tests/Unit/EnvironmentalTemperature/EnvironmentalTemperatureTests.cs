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
    /// <summary>The scale saturates independently of the numeric temperature, including below freezing.</summary>
    [Theory]
    [InlineData(-40, 0, "-40°C")] [InlineData(-20, 0, "-20°C")]
    [InlineData(10, .5f, "10°C")] [InlineData(40, 1, "40°C")] [InlineData(55, 1, "55°C")]
    public void MarkerClampsButReadingDoesNot(float celsius, float position, string expected)
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        string? reading = null;
        using var text = new HudOverlayIconTextPresentation((context, value) =>
        {
            reading = value;
            return new LoadedTexture(context.Api) { Width = 30, Height = 14 };
        });
        var gradient = new Mock<IEnvironmentalTemperatureScaleRenderer>();
        using var presentation = new EnvironmentalTemperaturePresentation(text, LoadTestTextures, _ => gradient.Object);
        presentation.SetContent(celsius);
        presentation.Prepare(new(api.Object, 1, "en", contentScale: 1.5));
        Assert.Equal(position, presentation.MarkerPosition);
        Assert.Equal(expected, reading);
        Assert.Equal(new SizeF(36, 24), presentation.Size);
        var bounds = ElementBounds.Fixed(0, 0, 54, 36).WithEmptyParent();
        bounds.absFixedX = bounds.absFixedY = 0;
        bounds.absInnerWidth = 54; bounds.absInnerHeight = 36; bounds.Initialized = true;
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 54, 36));
        gradient.Verify(value => value.Draw(renderer.Object, 1, new RectangleF(4.5f, 28.5f, 45, 3), 1), Times.Once);
        var marker = Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (int)call.Arguments[0] == 2);
        double x = (double)marker.Arguments[1], width = (double)marker.Arguments[3];
        Assert.InRange(x, 0, 54 - width);
        Assert.Equal(4.5 + position * 45, x + width / 2, 5);
    }

    /// <summary>Fractional changes move the marker while reusing text and borrowed icons until the rounded reading changes.</summary>
    [Fact]
    public void ReadingCacheAndOwnedLifetimeFollowDisplayedDegrees()
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        var readings = new List<LoadedTexture>();
        using var text = new HudOverlayIconTextPresentation((context, value) =>
        {
            var texture = new LoadedTexture(context.Api) { TextureId = 10 + readings.Count, Width = 30, Height = 14 };
            readings.Add(texture);
            return texture;
        });
        int loads = 0, gradientLoads = 0;
        var gradient = new Mock<IEnvironmentalTemperatureScaleRenderer>();
        using var presentation = new EnvironmentalTemperaturePresentation(text, context => { loads++; return LoadTestTextures(context); }, _ => { gradientLoads++; return gradient.Object; });
        var preparation = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.SetContent(12.1f); presentation.Prepare(preparation);
        float previous = presentation.MarkerPosition;
        presentation.SetContent(12.4f); presentation.Prepare(preparation);
        Assert.True(presentation.MarkerPosition > previous);
        Assert.Single(readings); Assert.Equal(1, loads); Assert.Equal(1, gradientLoads);
        presentation.SetContent(12.6f); presentation.Prepare(preparation);
        Assert.Equal(2, readings.Count); Assert.Equal(0, readings[0].TextureId);
        presentation.Hide();
        gradient.Verify(value => value.Dispose(), Times.Once);
        Assert.Equal(0, readings[1].TextureId); Assert.Equal(SizeF.Empty, presentation.Size);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Provides borrowed icon wrappers without a graphics context.</summary>
    private static LoadedTexture[] LoadTestTextures(HudOverlayPreparationContext context) =>
        [new LoadedTexture(context.Api) { TextureId = 1, IgnoreUndisposed = true },
         new LoadedTexture(context.Api) { TextureId = 2, IgnoreUndisposed = true }];
    #endregion
}
