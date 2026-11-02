using Cairo;
using System.Drawing;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Qualifies CPU-only canonical measurements against the installed native text boxing operation.</summary>
[Collection("HudOverlayGeometry")]
public sealed class NativeTextMetricsTests
{
    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0, 0)]
    [InlineData(.5, 1.5)]
    [InlineData(1, 1.5)]
    [InlineData(1.5, 1.5)]
    [InlineData(3, 3)]
    public void HudFontsPreserveDisabledAndWiderOutlines(double requested, double expected)
    {
        using var fixture = new HudNativeComponentFixture();
        var font = TextMetrics.Font(requested);
        Assert.Equal(expected, font.StrokeWidth);
        Assert.Equal(1.5, TextMetrics.Font().StrokeWidth);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1, .25, 1.5)]
    [InlineData(1.125, .25, 1.5)]
    [InlineData(2, 3, 1.5)]
    [InlineData(1.125, 0, 1.5)]
    [InlineData(1.125, 2, 0)]
    public void OutlineMinimumIsAppliedBeforeEveryFittingMeasurement(float scale, double stroke, double minimum)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        using var font = CairoFont.WhiteSmallText().WithFontSize(14).WithStroke(new double[] { 0, 0, 0, .85 }, stroke);
        font.GetFontExtents();
        int measurements = 0;
        CairoTextSizing.Fit(font, candidate =>
        {
            measurements++;
            double expected = stroke == 0 ? 0 : Math.Max(minimum, stroke * candidate.UnscaledFontsize / 14);
            Assert.Equal(expected, candidate.StrokeWidth, 5);
            var pixels = CairoTextSizing.PixelSize("Reading", candidate);
            Assert.True(pixels.Width > 0 && pixels.Height > 0);
            return candidate.UnscaledFontsize <= 7;
        }, minimum);
        Assert.True(measurements > 2);
        Assert.InRange(font.UnscaledFontsize, 6.99, 7);
        Assert.Equal(stroke == 0 ? 0 : Math.Max(minimum, stroke * font.UnscaledFontsize / 14), font.StrokeWidth, 5);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(1.125)]
    [InlineData(2)]
    public void TinyHudAperturesClipTheNaturalRasterWithoutReducingTheOutline(float scale)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            { texture.Width = surface.Width; texture.Height = surface.Height; }));
        var bounds = ElementBounds.Fixed(0, 0, 6 / scale, 6 / scale).WithEmptyParent();
        using var element = new TextElement(fixture.Api.Object, bounds, fixture.Failures.Add,
            (context, value, font) => TextDrawing.CreateTexture(context.Api, value, font));
        element.Prepare(fixture.Context, "Reading");
        bounds.CalcWorldBounds();
        element.LayoutCompleted(new(0, 0, 1000, 1000));
        var raster = CairoTextSizing.PixelSize("Reading", element.Font);
        Assert.Equal(1 / scale, element.Font.UnscaledFontsize, 5);
        Assert.Equal(1.5, element.Font.StrokeWidth);
        Assert.Equal(new SizeF(raster.Width, raster.Height), element.State.Target.Size);
        Assert.True(element.State.Target.Height > bounds.OuterHeight);
        element.RenderInteractiveElements(0);
        Assert.Contains(fixture.Render.Invocations, call => call.Method.Name == "Render2DTexturePremultipliedAlpha"
            && Convert.ToSingle(call.Arguments[3]) == raster.Width && Convert.ToSingle(call.Arguments[4]) == raster.Height);
        Assert.Empty(fixture.Failures);
    }

    [Fact]
    public void NativeFontSizeChangesItsMetrics()
    {
        using var fixture = new HudNativeComponentFixture();
        using var surface = new ImageSurface(Format.Argb32, 1, 1);
        using var drawing = new Context(surface);
        drawing.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Normal);
        drawing.SetFontSize(14);
        double original = drawing.FontExtents.Height;
        drawing.SetFontSize(7);
        double smaller = drawing.FontExtents.Height;
        Assert.True(smaller < original, $"Direct metrics {original} -> {smaller}");
        var font = CairoFont.WhiteSmallText().WithFontSize(14);
        original = font.GetFontExtents().Height;
        font.WithFontSize(7);
        smaller = font.GetFontExtents().Height;
        Assert.True(smaller < original, $"Wrapped metrics {original} -> {smaller}");
        font.WithFontSize(1);
        double tiny = font.GetFontExtents().Height;
        Assert.True(tiny < smaller, $"Tiny metrics {original} -> {smaller} -> {tiny}");
    }

    #region Public API
    /// <summary>Native text upload dimensions agree with canonical metrics across scale, stroke and multiline content.</summary>
    [Theory]
    [InlineData(1, 1, "125%")]
    [InlineData(1.5, 1.5, "HIGH")]
    [InlineData(2, 2.25, "Name\nReading")]
    public void MetricsMatchNativeGlyphBox(float scale, double stroke, string text)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                texture.Width = surface.Width; texture.Height = surface.Height;
            }));
        var predicted = TextMetrics.Measure(fixture.Context, text, stroke);
        using var native = new TextTextureUtil(fixture.Api.Object).GenTextTexture(text, TextMetrics.Font(stroke));
        Assert.Equal(native.Width / (double)scale, predicted.Width, 4);
        Assert.Equal(native.Height / (double)scale, predicted.Height, 4);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(1.125)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void CenteredReadingRetainsOrientationNaturalDimensionsAndCachedGeometry(float scale)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        int width = 0, height = 0, uploads = 0;
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                width = texture.Width = surface.Width; height = texture.Height = surface.Height;
                uploads++;
            }));
        var bounds = ElementBounds.Fixed(0, 0, 180, 100).WithEmptyParent();
        using var element = new TextElement(fixture.Api.Object, bounds, fixture.Failures.Add, strokeWidth: 1.5,
            orientation: EnumTextOrientation.Center);
        element.Prepare(fixture.Context, "88888\n8");
        bounds.CalcWorldBounds();
        element.LayoutCompleted(new(0, 0, 1000, 1000));
        Assert.Equal(new SizeF(width, height), element.State.Target.Size);
        Assert.Equal(EnumTextOrientation.Center, element.Font.Orientation);
        Assert.Equal(Math.Round(bounds.renderX + (bounds.OuterWidth - width) / 2), element.State.Target.X);
        Assert.Equal(Math.Round(bounds.renderY + (bounds.OuterHeight - height) / 2), element.State.Target.Y);
        int cachedUploads = uploads;
        element.Prepare(fixture.Context, "88888\n8");
        for (int frame = 0; frame < 4; frame++) element.RenderInteractiveElements(.1f);
        Assert.Equal(cachedUploads, uploads);
        Assert.Empty(fixture.Failures);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1.125, 1)]
    [InlineData(1.5, .5)]
    [InlineData(2, 2)]
    public void FittedHudTextDrawsNativePixelsAndCachesItsRaster(float scale, double indicatorScale)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            { texture.Width = surface.Width; texture.Height = surface.Height; }));
        var glyphs = new List<LoadedTexture>();
        var bounds = ElementBounds.Fixed(0, 0, 150 * indicatorScale, Math.Max(12 / scale, 12 * indicatorScale)).WithEmptyParent();
        using var element = new TextElement(fixture.Api.Object, bounds, fixture.Failures.Add,
            (context, value, font) =>
            {
                Assert.Equal(EnumTextOrientation.Center, font.Orientation);
                Assert.True(font.StrokeWidth >= 1.5);
                var raster = TextDrawing.CreateTexture(context.Api, value, font);
                var predicted = CairoTextSizing.PixelSize(value, font);
                Assert.Equal(predicted.Width, raster.Width);
                Assert.Equal(predicted.Height, raster.Height);
                glyphs.Add(raster);
                return raster;
            }, 1.5, orientation: EnumTextOrientation.Center);
        var context = fixture.Context.WithIndicatorScale(indicatorScale);
        element.Prepare(context, "125% AV");
        bounds.CalcWorldBounds();
        element.LayoutCompleted(new(0, 0, 1000, 1000));
        Assert.Equal(Status.Success, CairoFont.FontMeasuringContext.Status);
        var fitted = glyphs[^1];
        Assert.True(element.Font.UnscaledFontsize < 14 * indicatorScale);
        Assert.True(element.Font.StrokeWidth >= 1.5);
        Assert.Equal(new SizeF(fitted.Width, fitted.Height), element.State.Target.Size);
        Assert.True(fitted.Width <= bounds.OuterWidth);
        Assert.True(fitted.Height <= bounds.OuterHeight, $"Font {element.Font.UnscaledFontsize}: raster {fitted.Width}x{fitted.Height}, aperture {bounds.OuterWidth}x{bounds.OuterHeight}");
        Assert.Equal(Math.Round(element.State.Target.X), element.State.Target.X);
        int creates = glyphs.Count;
        for (int frame = 0; frame < 4; frame++) element.RenderInteractiveElements(.1f);
        Assert.Contains(fixture.Render.Invocations, call => call.Method.Name == "Render2DTexturePremultipliedAlpha"
            && Convert.ToSingle(call.Arguments[3]) == fitted.Width && Convert.ToSingle(call.Arguments[4]) == fitted.Height);
        bounds.fixedX += 7; bounds.MarkDirtyRecursive(); bounds.CalcWorldBounds();
        element.LayoutCompleted(new(0, 0, 1000, 1000));
        element.Prepare(context, "125% AV");
        Assert.Equal(creates, glyphs.Count);
        bounds.fixedWidth = 1000; bounds.fixedHeight = 100;
        bounds.MarkDirtyRecursive(); bounds.CalcWorldBounds();
        element.LayoutCompleted(new(0, 0, 1000, 1000));
        Assert.Equal(14 * indicatorScale, element.Font.UnscaledFontsize, 4);
        Assert.True(fitted.Disposed);
        Assert.Equal(new SizeF(glyphs[^1].Width, glyphs[^1].Height), element.State.Target.Size);
        element.Dispose();
        Assert.All(glyphs, glyph => Assert.True(glyph.Disposed));
        Assert.Empty(fixture.Failures);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(1.5, 100)]
    [InlineData(2, 80)]
    public void RadialCenterLabelsFitByFontSizeInsteadOfImageScale(float scale, int diameter)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        var font = CairoFont.WhiteSmallText().WithFontSize(20).WithStroke(new double[] { 0, 0, 0, 1 }, 1.5);
        string wrapped = RadialMenuLabelLayout.FitFontToCircle("A very long localized center menu label", diameter, font, 2);
        Assert.True(font.UnscaledFontsize < 20);
        double height = Math.Ceiling(font.GetFontExtents().Height);
        double fit = RadialMenuLabelLayout.GetScaleForCircle(wrapped.Split('\n'), diameter, height,
            line => RadialMenuLabelLayout.LineWidth(font, line), 2);
        Assert.True(fit == 1, $"Font {font.UnscaledFontsize}, height {height}, fit {fit}: {wrapped}");
        Assert.Equal("A very long localized center menu label", wrapped.Replace('\n', ' '));
    }

    [Fact]
    public void FailedFittedRasterPreservesOwnershipAndRetriesLayout()
    {
        using var fixture = new HudNativeComponentFixture();
        var original = new LoadedTexture(fixture.Api.Object, 100, 100, 40);
        var fitted = new LoadedTexture(fixture.Api.Object, 101, 80, 8);
        bool fail = true;
        int creates = 0;
        var bounds = ElementBounds.Fixed(0, 0, 100, 12).WithEmptyParent();
        using var element = new TextElement(fixture.Api.Object, bounds, fixture.Failures.Add,
            (_, _, _) => ++creates == 1 ? original : fail ? throw new InvalidOperationException("raster failure") : fitted);
        element.Prepare(fixture.Context, "READING");
        bounds.CalcWorldBounds();
        Assert.Throws<InvalidOperationException>(() => element.LayoutCompleted(new(0, 0, 200, 200)));
        Assert.False(original.Disposed);
        fail = false;
        element.LayoutCompleted(new(0, 0, 200, 200));
        Assert.True(original.Disposed);
        Assert.Equal(new SizeF(100, 40), element.Size);
        Assert.Equal(new SizeF(80, 8), element.State.Target.Size);
        element.Dispose();
        Assert.True(fitted.Disposed);
    }
    #endregion

    #region Private
    /// <summary>Matches the native ref-texture upload seam without creating a graphics context.</summary>
    private delegate void Upload(ImageSurface surface, bool linear, ref LoadedTexture texture);
    #endregion
}
