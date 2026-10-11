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
public sealed class BodyTemperatureThermometerTests
{
    #region Public API
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
    #region Rendering and resource lifecycle
    /// <summary>Asset cache bindings survive scale and content changes and reload only for a different native owner.</summary>
    [Fact]
    public void AssetBindingsAreBorrowedAndReuseScaleChanges()
    {
        var api = new Mock<ICoreClientAPI>(); var renderer = new Mock<IRenderAPI>(); var gui = new Mock<IGuiAPI>();
        api.SetupGet(value => value.Render).Returns(renderer.Object); api.SetupGet(value => value.Gui).Returns(gui.Object);
        var borrowed = new List<LoadedTexture>();
        renderer.Setup(value => value.GetOrLoadTexture(It.IsAny<Vintagestory.API.Common.AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback((Vintagestory.API.Common.AssetLocation location, ref LoadedTexture texture) =>
            { texture = new LoadedTexture(api.Object) { TextureId = 7 }; borrowed.Add(texture); });
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation());
        presentation.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), false);
        presentation.Prepare(new(api.Object, 1, "en"));
        Assert.Equal(new SizeF(14, 24), presentation.Size);
        presentation.SetContent(new(45, 37, BodyTemperatureRisk.Overheating), false);
        presentation.Prepare(new(api.Object, 2, "en"));
        var loads = renderer.Invocations.Where(call => call.Method.Name == "GetOrLoadTexture").ToArray();
        Assert.Equal(4, loads.Length);
        Assert.Equal(new[] { "vanillaexpanded:textures/hud/body-temperature/outline.png", "vanillaexpanded:textures/hud/body-temperature/fill-mask.png", "vanillaexpanded:textures/hud/body-temperature/chevron-up.png", "vanillaexpanded:textures/hud/body-temperature/chevron-down.png" },
            loads.Select(call => call.Arguments[0].ToString()));
        var replacementApi = new Mock<ICoreClientAPI>(); replacementApi.SetupGet(value => value.Render).Returns(renderer.Object);
        presentation.Prepare(new(replacementApi.Object, 2, "en"));
        Assert.Equal(8, renderer.Invocations.Count(call => call.Method.Name == "GetOrLoadTexture"));
        presentation.Reset(); presentation.Dispose(); Assert.Equal(SizeF.Empty, presentation.Size);
        Assert.Equal(8, borrowed.Count); Assert.All(borrowed, texture => { Assert.False(texture.Disposed); Assert.Equal(7, texture.TextureId); Assert.True(texture.IgnoreUndisposed); });
        gui.Verify(value => value.DeleteTexture(It.IsAny<int>()), Times.Never);
    }
    /// <summary>Minimum, midpoint, and maximum fills crop the same full mask at both GUI scales.</summary>
    [Theory]
    [InlineData(1, 0)] [InlineData(1, .5f)] [InlineData(1, 1)]
    [InlineData(2, 0)] [InlineData(2, .5f)] [InlineData(2, 1)]
    public void DrawCropsFullMaskAtScaledFill(double scale, float fill)
    {
        var api = new Mock<ICoreClientAPI>();
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(), context => [new LoadedTexture(context.Api) { TextureId = 1, IgnoreUndisposed = true }, new LoadedTexture(context.Api) { TextureId = 2, IgnoreUndisposed = true }]);
        // Supply raw simulation values corresponding to the selected converted Celsius fill.
        float celsius = 31 + fill * 14;
        float raw = celsius > 37 ? 37 + (celsius - 37) * 10 : celsius;
        presentation.SetContent(new(raw, 37, BodyTemperatureRisk.Freezing, 1), false);
        presentation.Prepare(new(api.Object, scale, "en"));
        var bounds = Bounds(scale); var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), .1f);
        var calls = renderer.Invocations.ToArray();
        Assert.Equal(new[] { "PushScissor", "RenderTexture", "PopScissor", "RenderTexture" }, calls.Select(call => call.Method.Name));
        var crop = Assert.IsAssignableFrom<ElementBounds>(calls[0].Arguments[0]);
        Assert.Equal(100, crop.renderX); Assert.Equal(200 + 15.5 * (1 - fill) * scale, crop.renderY, 6);
        Assert.Equal(14 * scale, crop.OuterWidth); Assert.Equal((24 - 15.5 * (1 - fill)) * scale, crop.OuterHeight, 6);
        var mask = calls[1].Arguments; Assert.Equal(2, mask[0]); Assert.Equal(100d, mask[1]); Assert.Equal(200d, mask[2]);
        Assert.Equal(14 * scale, mask[3]); Assert.Equal(24 * scale, mask[4]);
        var color = Assert.IsType<Vec4f>(mask[6]); Assert.Equal(.25f, color.R, 5); Assert.Equal(.6f, color.G, 5);
        Assert.Equal(1, calls[3].Arguments[0]);
    }
    /// <summary>Trend textures stay within measured bounds and reverse without moving the thermometer.</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(-1)] [InlineData(-2)]
    public void TrendChevronsRespectMeasuredBounds(int trend)
    {
        var api = new Mock<ICoreClientAPI>();
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(),
            context => Enumerable.Range(1, 4).Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray());
        presentation.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, trend), false);
        presentation.Prepare(new(api.Object, 1, "en"));
        Assert.Equal(new SizeF(14, 46), presentation.Size);
        var bounds = Bounds(1);
        bounds.absInnerHeight = presentation.Size.Height;
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), .1f);
        var chevrons = renderer.Invocations.Where(call => call.Method.Name == "RenderTexture" && (int)call.Arguments[0] == (trend > 0 ? 3 : 4)).ToArray();
        Assert.Equal(Math.Abs(trend), chevrons.Length);
        foreach (var call in chevrons)
        {
            double y = (double)call.Arguments[2];
            Assert.InRange(y, bounds.renderY, bounds.renderY + bounds.OuterHeight - 4);
            Assert.True(trend > 0 ? y < bounds.renderY + 11 : y > bounds.renderY + 35);
        }
    }

    /// <summary>The fill scissor intersects the host member clip and always restores it when rendering fails.</summary>
    [Fact]
    public void FillCropIntersectsHostAndRestoresAfterFailure()
    {
        var api = new Mock<ICoreClientAPI>();
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(), context => [new LoadedTexture(context.Api) { TextureId = 1, IgnoreUndisposed = true }, new LoadedTexture(context.Api) { TextureId = 2, IgnoreUndisposed = true }]);
        presentation.SetContent(new(38, 37, BodyTemperatureRisk.Cold, .25f), false);
        presentation.Prepare(new(api.Object, 1, "en"));
        var renderer = new Mock<IRenderAPI>();
        renderer.Setup(value => value.RenderTexture(2, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>())).Throws(new InvalidOperationException("mask render failed"));
        Assert.Throws<InvalidOperationException>(() => presentation.Draw(renderer.Object, Bounds(1), new RectangleF(105, 210, 5, 10), .1f));
        var calls = renderer.Invocations.ToArray();
        Assert.Equal(new[] { "PushScissor", "RenderTexture", "PopScissor" }, calls.Select(call => call.Method.Name));
        var crop = Assert.IsAssignableFrom<ElementBounds>(calls[0].Arguments[0]);
        Assert.Equal(105, crop.renderX); Assert.Equal(210, crop.renderY); Assert.Equal(5, crop.OuterWidth); Assert.Equal(10, crop.OuterHeight);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Creates initialized pixel bounds independent of global GUI scale.</summary>
    private static ElementBounds Bounds(double scale)
    {
        var bounds = ElementBounds.Fixed(0, 0, 14, 24).WithEmptyParent();
        bounds.absFixedX = 100; bounds.absFixedY = 200; bounds.absInnerWidth = 14 * scale; bounds.absInnerHeight = 24 * scale; bounds.Initialized = true;
        return bounds;
    }
    #endregion
}
