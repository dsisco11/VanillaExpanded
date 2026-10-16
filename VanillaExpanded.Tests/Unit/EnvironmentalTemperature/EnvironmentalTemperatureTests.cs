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
    /// <summary>Layer placement, radial calibration, and the numeric reading stay independent of GUI scale.</summary>
    [Theory]
    [InlineData(-55, 0, "-55°C", 1)]
    [InlineData(-40, 0, "-40°C", 2)]
    [InlineData(0, .4f, "0°C", 1.5)]
    [InlineData(9, .49f, "9°C", 1)]
    [InlineData(60, 1, "60°C", 2)]
    [InlineData(75, 1, "75°C", 1)]
    public void FillClampsWhileReadingRemainsActual(float celsius, float fill, string expected, double scale)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        string? reading = null;
        using var text = new WorkshopGaugeText((context, value) =>
        {
            reading = value;
            return new LoadedTexture(context.Api) { TextureId = 20, Width = (int)(30 * scale), Height = (int)(14 * scale) };
        });
        using var presentation = new EnvironmentalTemperaturePresentation(text, LoadTestTextures,
            context => new LoadedTexture(context.Api) { TextureId = 10 }, CreateLiquid());
        presentation.SetContent(celsius); presentation.Prepare(new(api.Object, scale, "en"));
        Assert.Equal(fill, presentation.MarkerPosition, 5); Assert.Equal(expected, reading);
        Assert.Equal(new SizeF(HudOverlaySizes.EnvironmentalThermometer, HudOverlaySizes.EnvironmentalThermometer), presentation.Size);
        double pixels = HudOverlaySizes.EnvironmentalThermometer * scale;
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, Bounds(10, 20, pixels, pixels), new(10, 20, (float)pixels, (float)pixels));
        var layers = renderer.Invocations.Where(call => call.Method.Name == "RenderTexture").ToArray();
        Assert.Equal(new[] { 1, 0, 3, 4, 5, 2 }, layers.Select(call => (int)call.Arguments[0]));
        foreach (var layer in layers.Where(call => (int)call.Arguments[0] <= 2))
        {
            Assert.Equal(10d, layer.Arguments[1]); Assert.Equal(20d, layer.Arguments[2]);
            Assert.Equal(pixels, layer.Arguments[3]); Assert.Equal(pixels, layer.Arguments[4]);
        }
        Assert.Equal(53f, layers.Last().Arguments[5]);
        var number = Assert.Single(renderer.Invocations, call => call.Method.Name == "Render2DTexturePremultipliedAlpha" && (int)call.Arguments[0] == 20);
        Assert.Equal(10 + pixels / 2, (double)number.Arguments[1] + (double)number.Arguments[3] / 2, 4);
        Assert.Equal(20 + pixels / 2, (double)number.Arguments[2] + (double)number.Arguments[4] / 2, 4);
        Assert.Equal(14 * scale, (double)number.Arguments[4]);
    }

    /// <summary>Fractional temperatures move liquid while rounded values reuse reading and tick textures.</summary>
    [Fact]
    public void ReadingCacheAndOwnedLifetimeFollowDisplayedDegrees()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var readings = new List<LoadedTexture>();
        var borrowed = LoadTestTextures(new(api.Object, 1, "en"));
        var ticks = new LoadedTexture(api.Object) { TextureId = 99 };
        using var text = new WorkshopGaugeText((context, _) =>
        {
            var texture = new LoadedTexture(context.Api) { TextureId = 10 + readings.Count, Width = 30, Height = 14 };
            readings.Add(texture); return texture;
        });
        int loads = 0, tickLoads = 0;
        using var presentation = new EnvironmentalTemperaturePresentation(text, _ => { loads++; return borrowed; },
            _ => { tickLoads++; return ticks; }, CreateLiquid());
        var preparation = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.SetContent(12.1f); presentation.Prepare(preparation);
        float previous = presentation.MarkerPosition;
        presentation.SetContent(12.4f); presentation.Prepare(preparation);
        Assert.True(presentation.MarkerPosition > previous);
        Assert.Single(readings); Assert.Equal(1, loads); Assert.Equal(1, tickLoads);
        presentation.SetContent(12.6f); presentation.Prepare(preparation);
        Assert.Equal(2, readings.Count); Assert.True(readings[0].Disposed);
        presentation.Hide();
        Assert.True(ticks.Disposed); Assert.True(readings[1].Disposed);
        Assert.All(borrowed, texture => Assert.False(texture.Disposed)); Assert.Equal(SizeF.Empty, presentation.Size);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Provides borrowed backing, cover, and scale labels without a graphics context.</summary>
    private static LoadedTexture[] LoadTestTextures(HudOverlayPreparationContext context) => Enumerable.Range(1, 5)
        .Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray();

    /// <summary>Provides a headless alpha-mask resource with transparent source pixels and no native filtering.</summary>
    private static EnvironmentalTemperatureLiquidMask CreateLiquid() => new(_ => new int[512 * 512], texture => texture);

    /// <summary>Creates initialized native screen bounds without depending on GUI scale globals.</summary>
    private static ElementBounds Bounds(double x, double y, double width, double height)
    {
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
        bounds.absFixedX = x; bounds.absFixedY = y;
        bounds.absInnerWidth = width; bounds.absInnerHeight = height; bounds.Initialized = true;
        return bounds;
    }
    #endregion
}
