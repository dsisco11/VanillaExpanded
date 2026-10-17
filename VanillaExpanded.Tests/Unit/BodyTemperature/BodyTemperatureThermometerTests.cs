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
    #region Rendering and resource lifecycle
    /// <summary>Asset cache bindings survive scale and content changes and reload only for a different native owner.</summary>
    [Fact]
    public void AssetBindingsAreBorrowedAndReuseScaleChanges()
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock }; var renderer = new Mock<IRenderAPI>(); var gui = new Mock<IGuiAPI>();
        api.SetupGet(value => value.Render).Returns(renderer.Object); api.SetupGet(value => value.Gui).Returns(gui.Object);
        // Mocked uploads allocate no OpenGL handles; retain native cache and borrowed-lifetime checks.
        var borrowed = new List<LoadedTexture>();
        renderer.Setup(value => value.GetOrLoadTexture(It.IsAny<Vintagestory.API.Common.AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback((Vintagestory.API.Common.AssetLocation location, ref LoadedTexture texture) =>
            { texture = new LoadedTexture(api.Object) { TextureId = 0 }; borrowed.Add(texture); });
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation());
        presentation.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), false);
        presentation.Prepare(new(api.Object, 1, "en"));
        Assert.NotEqual(SizeF.Empty, presentation.Size);
        presentation.SetContent(new(45, 37, BodyTemperatureRisk.Overheating), false);
        presentation.Prepare(new(api.Object, 2, "en"));
        var loads = renderer.Invocations.Where(call => call.Method.Name == "GetOrLoadTexture").ToArray();
        Assert.Equal(4, loads.Length);
        Assert.Equal(new[] { "vanillaexpanded:textures/hud/body-temperature/outline.png", "vanillaexpanded:textures/hud/body-temperature/fill-mask.png", "vanillaexpanded:textures/hud/body-temperature/chevron-up.png", "vanillaexpanded:textures/hud/body-temperature/chevron-down.png" },
            loads.Select(call => call.Arguments[0].ToString()));
        var replacementApi = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock }; replacementApi.SetupGet(value => value.Render).Returns(renderer.Object);
        presentation.Prepare(new(replacementApi.Object, 2, "en"));
        Assert.Equal(8, renderer.Invocations.Count(call => call.Method.Name == "GetOrLoadTexture"));
        presentation.Reset(); presentation.Dispose(); Assert.Equal(SizeF.Empty, presentation.Size);
        Assert.Equal(8, borrowed.Count); Assert.All(borrowed, texture => { Assert.False(texture.Disposed); Assert.Equal(0, texture.TextureId); Assert.True(texture.IgnoreUndisposed); });
        gui.Verify(value => value.DeleteTexture(It.IsAny<int>()), Times.Never);
    }
    /// <summary>Minimum, midpoint, and maximum fills crop the same full mask at both GUI scales.</summary>
    [Theory]
    [InlineData(1, 0)] [InlineData(1, .5f)] [InlineData(1, 1)]
    [InlineData(2, 0)] [InlineData(2, .5f)] [InlineData(2, 1)]
    public void DrawCropsFullMaskAtScaledFill(double scale, float fill)
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(), context => [new LoadedTexture(context.Api) { TextureId = 1, IgnoreUndisposed = true }, new LoadedTexture(context.Api) { TextureId = 2, IgnoreUndisposed = true }]);
        // Supply raw simulation values corresponding to the selected converted Celsius fill.
        float celsius = 31 + fill * 14;
        float raw = celsius > 37 ? 37 + (celsius - 37) * 10 : celsius;
        presentation.SetContent(new(raw, 37, BodyTemperatureRisk.Freezing, 1), false);
        presentation.Prepare(new(api.Object, scale, "en"));
        var bounds = Bounds(scale); var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), .1f);
        var calls = renderer.Invocations.Where(call => call.Method.Name != "RenderTexture" || (float)call.Arguments[5] != 49).ToArray();
        Assert.Equal(new[] { "PushScissor", "RenderTexture", "PopScissor", "RenderTexture" }, calls.Select(call => call.Method.Name));
        var crop = Assert.IsAssignableFrom<ElementBounds>(calls[0].Arguments[0]);
        var mask = calls[1].Arguments;
        Assert.Equal(2, mask[0]);
        Assert.InRange(crop.renderX, (double)mask[1], (double)mask[1] + (double)mask[3]);
        Assert.InRange(crop.renderY, (double)mask[2], (double)mask[2] + (double)mask[4]);
        Assert.True(crop.renderX + crop.OuterWidth <= (double)mask[1] + (double)mask[3]);
        Assert.True(crop.renderY + crop.OuterHeight <= (double)mask[2] + (double)mask[4]);
        Assert.Equal(1, calls[3].Arguments[0]);
    }
    /// <summary>Trend textures stay within measured bounds and reverse without moving the thermometer.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(-1)] [InlineData(-2)]
    public void TrendChevronsRespectMeasuredBounds(int trend)
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(),
            context => Enumerable.Range(1, 4).Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray());
        presentation.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, trend), false);
        presentation.Prepare(new(api.Object, 1, "en"));
        var bounds = Bounds(1);
        bounds.absInnerWidth = presentation.Size.Width;
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), .1f);
        var outline = Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (int)call.Arguments[0] == 1);
        Assert.Equal(bounds.renderY, (double)outline.Arguments[2]);
        var chevrons = renderer.Invocations.Where(call => call.Method.Name == "RenderTexture" && (int)call.Arguments[0] == (trend > 0 ? 3 : 4)).ToArray();
        Assert.Equal(Math.Abs(trend), chevrons.Length);
        for (int index = 0; index < chevrons.Length; index++)
        {
            var call = chevrons[index];
            double y = (double)call.Arguments[2];
            Assert.InRange(y, bounds.renderY, bounds.renderY + bounds.OuterHeight - (double)call.Arguments[4]);
            if (index > 0) Assert.True(y > (double)chevrons[index - 1].Arguments[2]);
            Assert.Equal(chevrons[0].Arguments[1], call.Arguments[1]);
            Assert.True((double)call.Arguments[1] >= bounds.renderX);
        }
    }

    /// <summary>Stacked trends stay beside the thermometer and outside fitted text at different GUI scales.</summary>
    [Theory]
    [InlineData(1, 2)] [InlineData(1.5, -2)] [InlineData(2, 1)] [InlineData(2, -1)]
    public void ChevronsAndTallReadingFitTheSameIconHeight(double scale, int trend)
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        var text = new HudOverlayIconTextPresentation((context, _) => new LoadedTexture(context.Api)
            { TextureId = 5, Width = (int)(28 * scale), Height = (int)(40 * scale), IgnoreUndisposed = true });
        using var presentation = new BodyTemperatureThermometerPresentation(text,
            context => Enumerable.Range(1, 4).Select(id => new LoadedTexture(context.Api) { TextureId = id, IgnoreUndisposed = true }).ToArray());
        presentation.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, trend), true);
        presentation.Prepare(new(api.Object, scale, "en"));
        var bounds = Bounds(scale);
        bounds.absInnerWidth = presentation.Size.Width * scale;
        var renderer = new Mock<IRenderAPI>();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 1000, 1000), .1f);
        var reading = Assert.Single(renderer.Invocations, call => call.Method.Name == "Render2DTexturePremultipliedAlpha");
        double textTop = (double)reading.Arguments[2], textBottom = textTop + (double)reading.Arguments[4];
        Assert.InRange(textTop, bounds.renderY, bounds.renderY + bounds.OuterHeight);
        Assert.InRange(textBottom, bounds.renderY, bounds.renderY + bounds.OuterHeight);
        foreach (var arrow in renderer.Invocations.Where(call => call.Method.Name == "RenderTexture" && (int)call.Arguments[0] == (trend > 0 ? 3 : 4)))
        {
            double arrowTop = (double)arrow.Arguments[2], arrowBottom = arrowTop + (double)arrow.Arguments[4];
            Assert.True(trend > 0 ? arrowBottom < textTop : arrowTop > textBottom);
            Assert.InRange((double)arrow.Arguments[1], bounds.renderX, bounds.renderX + bounds.OuterWidth - (double)arrow.Arguments[3]);
        }
    }

    /// <summary>The fill scissor intersects the host member clip and always restores it when rendering fails.</summary>
    [Fact]
    public void FillCropIntersectsHostAndRestoresAfterFailure()
    {
        var api = new Mock<ICoreClientAPI>() { DefaultValue = DefaultValue.Mock };
        using var presentation = new BodyTemperatureThermometerPresentation(new HudOverlayIconTextPresentation(), context => [new LoadedTexture(context.Api) { TextureId = 1, IgnoreUndisposed = true }, new LoadedTexture(context.Api) { TextureId = 2, IgnoreUndisposed = true }]);
        presentation.SetContent(new(38, 37, BodyTemperatureRisk.Cold, .25f), false);
        presentation.Prepare(new(api.Object, 1, "en"));
        var renderer = new Mock<IRenderAPI>();
        renderer.Setup(value => value.RenderTexture(2, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>())).Throws(new InvalidOperationException("mask render failed"));
        var hostClip = new RectangleF(105, 220, 5, 10);
        Assert.Throws<InvalidOperationException>(() => presentation.Draw(renderer.Object, Bounds(1), hostClip, .1f));
        var calls = renderer.Invocations.Where(call => call.Method.Name != "RenderTexture" || (float)call.Arguments[5] != 49).ToArray();
        Assert.Equal(new[] { "PushScissor", "RenderTexture", "PopScissor" }, calls.Select(call => call.Method.Name));
        var crop = Assert.IsAssignableFrom<ElementBounds>(calls[0].Arguments[0]);
        Assert.InRange(crop.renderX, hostClip.Left, hostClip.Right);
        Assert.InRange(crop.renderY, hostClip.Top, hostClip.Bottom);
        Assert.True(crop.renderX + crop.OuterWidth <= hostClip.Right);
        Assert.True(crop.renderY + crop.OuterHeight <= hostClip.Bottom);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Creates initialized pixel bounds independent of global GUI scale.</summary>
    private static ElementBounds Bounds(double scale)
    {
        var bounds = ElementBounds.Fixed(0, 0, 21, 36).WithEmptyParent();
        bounds.absFixedX = 100; bounds.absFixedY = 200; bounds.absInnerWidth = 21 * scale; bounds.absInnerHeight = 36 * scale; bounds.Initialized = true;
        return bounds;
    }
    #endregion
}
