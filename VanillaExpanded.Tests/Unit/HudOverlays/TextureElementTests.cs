using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies native-pixel SVG layout, default rasterization, borrowing and generated-texture ownership.</summary>
[Trait("Category", "Unit")]
[Collection("HudOverlayGeometry")]
public sealed class TextureElementTests
{
    #region Public API
    /// <summary>SVG preparation defers rasterization until resolved bounds and draws the truncated physical pixel canvas 1:1.</summary>
    [Fact]
    public void SvgRasterizesAfterLayoutAndDrawsTheTruncatedPhysicalCanvasAtNativeSize()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture(scale: 1.5f);
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string path = "textures/hud/body-temperature/fill-mask.svg";
        using var element = CreateElement(fixture, path, 10.5, -3.5, 6, 8);

        // Act
        element.Prepare(fixture.Context, path);
        element.Prepare(fixture.Context, path);

        // Assert
        Assert.Empty(loader.Requests);

        // Act
        element.LayoutCompleted(new RectangleF(11, -4, 4, 5));

        // Assert
        var request = Assert.Single(loader.Requests);
        Assert.Equal(new SvgRasterizationRecorder.Request(path, 6, 7, 6, 7), request);
        var texture = Assert.Single(loader.Textures);
        Assert.Equal(new Size(request.Width, request.Height), new Size(texture.Width, texture.Height));
        Assert.Equal(new RectangleF(10, -3, 6, 7), element.State.Target);
        Assert.Empty(fixture.Failures);

        // Act
        texture.TextureId = 701;
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);

        // Assert
        var draw = Assert.Single(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Equal(texture.TextureId, (int)draw.Arguments[0]);
        Assert.Equal(10d, Convert.ToDouble(draw.Arguments[1]));
        Assert.Equal(-3d, Convert.ToDouble(draw.Arguments[2]));
        Assert.Equal(6d, Convert.ToDouble(draw.Arguments[3]));
        Assert.Equal(7d, Convert.ToDouble(draw.Arguments[4]));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>An owned SVG supplied by an injected loader uses the resolved pixel canvas and remains a 1:1 draw.</summary>
    [Fact]
    public void InjectedOwnedSvgLoaderUsesResolvedPixelSizeAndNativeDrawGeometry()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        const string path = "textures/hud/test/custom.svg";
        var requests = new List<(string Path, Size Size)>();
        var texture = new LoadedTexture(fixture.Api.Object) { TextureId = 0, Width = 5, Height = 5 };
        using var element = new TextureElement(fixture.Api.Object, AbsoluteBounds(-2.75, 4.5, 6.25, 4.5),
            fixture.Failures.Add, (_, assetPath, size) =>
            {
                requests.Add((assetPath, size));
                return new(texture, true);
            });
        element.Prepare(fixture.Context, path);

        // Act
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));

        // Assert
        Assert.Equal([(path, new Size(5, 5))], requests);
        Assert.Equal(new RectangleF(-2, 4, 5, 5), element.State.Target);
        Assert.False(texture.Disposed);

        // Act
        texture.TextureId = 702;
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);

        // Assert
        var draw = Assert.Single(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Equal(texture.TextureId, (int)draw.Arguments[0]);
        Assert.Equal(-2d, Convert.ToDouble(draw.Arguments[1]));
        Assert.Equal(4d, Convert.ToDouble(draw.Arguments[2]));
        Assert.Equal(5d, Convert.ToDouble(draw.Arguments[3]));
        Assert.Equal(5d, Convert.ToDouble(draw.Arguments[4]));

        // Act
        element.Dispose();

        // Assert
        Assert.True(texture.Disposed);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Movement, clipping and changing tint/opacity reuse an unchanged SVG canvas and never scale its draw.</summary>
    [Fact]
    public void MovementClippingAndVisualStateReuseTheFullNativeRaster()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture(scale: 1.25f);
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string path = "textures/hud/weather/composite/rain-light.svg";
        using var element = CreateElement(fixture, path, 10.5, 20.25, 16.25, 23.5);
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        var texture = Assert.Single(loader.Textures);
        texture.TextureId = 711;
        int initialRequests = loader.Requests.Count;

        // Act
        SetBounds(element, 12.5, 21.25, 16.25, 23.5);
        var narrowerClip = new RectangleF(13, 22, 7, 9);
        element.LayoutCompleted(narrowerClip);
        element.State.SetColor(.25f, .5f, .75f);
        element.State.SetOpacity(.6f);
        element.RenderInteractiveElements(0);

        // Assert
        Assert.Equal(initialRequests, loader.Requests.Count);
        Assert.Equal(new RectangleF(12, 21, 16, 23), element.State.Target);
        var draw = Assert.Single(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Equal(texture.TextureId, (int)draw.Arguments[0]);
        Assert.Equal(12d, Convert.ToDouble(draw.Arguments[1]));
        Assert.Equal(21d, Convert.ToDouble(draw.Arguments[2]));
        Assert.Equal(texture.Width, Convert.ToDouble(draw.Arguments[3]));
        Assert.Equal(texture.Height, Convert.ToDouble(draw.Arguments[4]));
        Assert.NotNull(draw.Arguments[6]);
        var clip = Assert.Single(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.PushScissor));
        var scissor = Assert.IsType<ElementBounds>(clip.Arguments[0]);
        Assert.Equal(narrowerClip.X, scissor.renderX);
        Assert.Equal(narrowerClip.Y, scissor.renderY);
        Assert.Equal(narrowerClip.Width, scissor.OuterWidth);
        Assert.Equal(narrowerClip.Height, scissor.OuterHeight);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Adjacent texture layers truncate their common fractional edge identically without a gap or overlap.</summary>
    [Fact]
    public void AdjacentSvgLayersShareTruncatedPhysicalEdges()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        using var fill = CreateElement(fixture, "textures/hud/test/fill.svg", .25, 2.25, 10.25, 8);
        using var outline = CreateElement(fixture, "textures/hud/test/outline.svg", 10.5, 2.25, 8.5, 8);

        // Act
        fill.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        outline.LayoutCompleted(new RectangleF(0, 0, 100, 100));

        // Assert
        Assert.Equal(10, fill.State.Target.Right);
        Assert.Equal(fill.State.Target.Right, outline.State.Target.Left);
        Assert.Equal(19, outline.State.Target.Right);
        Assert.Equal(new[] { (10, 8), (9, 8) }, loader.Requests.Select(request => (request.Width, request.Height)));
        Assert.Equal(loader.Requests.Select(request => (request.Width, request.Height)),
            loader.Textures.Select(texture => (texture.Width, texture.Height)));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Equal assets at different sizes are independent, while same-instance size changes replace before drawing.</summary>
    [Fact]
    public void SameAssetUsesIndependentSizeAwareRastersAndReplacesOnResize()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string path = "textures/hud/calendar/date.svg";
        using var first = CreateElement(fixture, path, 0, 0, 13, 19);
        using var second = CreateElement(fixture, path, 30, 5, 8, 11);

        // Act
        first.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        second.LayoutCompleted(new RectangleF(0, 0, 100, 100));

        // Assert
        Assert.Equal(new[]
        {
            new SvgRasterizationRecorder.Request(path, 13, 19, 13, 19),
            new SvgRasterizationRecorder.Request(path, 8, 11, 8, 11)
        }, loader.Requests);
        var firstRaster = loader.Textures[0];
        var secondRaster = loader.Textures[1];
        Assert.NotSame(firstRaster, secondRaster);
        firstRaster.TextureId = 721;
        secondRaster.TextureId = 722;

        // Act
        SetBounds(first, 0, 0, 21, 17);
        first.LayoutCompleted(new RectangleF(0, 0, 100, 100));

        // Assert
        Assert.True(firstRaster.Disposed);
        Assert.False(secondRaster.Disposed);
        var replacement = loader.Textures[2];
        Assert.Equal(new Size(21, 17), new Size(replacement.Width, replacement.Height));
        Assert.Equal(new SvgRasterizationRecorder.Request(path, 21, 17, 21, 17), loader.Requests[^1]);
        replacement.TextureId = 723;
        fixture.Render.Invocations.Clear();

        // Act
        first.RenderInteractiveElements(0);
        second.RenderInteractiveElements(0);

        // Assert
        var draws = fixture.Render.Invocations
            .Where(call => call.Method.Name == nameof(IRenderAPI.RenderTexture)).ToArray();
        Assert.Equal(2, draws.Length);
        Assert.Equal(new[] { 21d, 8d }, draws.Select(call => Convert.ToDouble(call.Arguments[3])));
        Assert.Equal(new[] { 17d, 11d }, draws.Select(call => Convert.ToDouble(call.Arguments[4])));
        first.Dispose();
        Assert.True(replacement.Disposed);
        Assert.False(secondRaster.Disposed);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>A failed new raster remains non-drawable and retires the rejected resource and prior owner on cleanup.</summary>
    [Fact]
    public void WrongSizedReplacementCannotDrawStaleSvgAndIsCleanedUp()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string path = "textures/hud/weather/composite/snow-light.svg";
        using var element = CreateElement(fixture, path, 0, 0, 12, 14);
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        var previous = Assert.Single(loader.Textures);
        previous.TextureId = 731;
        loader.CreateTexture = request => new LoadedTexture(fixture.Api.Object)
        {
            TextureId = 0,
            Width = request.Width + 1,
            Height = request.Height
        };
        SetBounds(element, 0, 0, 18, 15);

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => element.LayoutCompleted(new RectangleF(0, 0, 100, 100)));

        // Assert
        Assert.Contains("18 x 15", error.Message);
        var rejected = loader.Textures[^1];
        Assert.True(rejected.Disposed);
        Assert.False(previous.Disposed);
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);
        Assert.DoesNotContain(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));

        // Act
        element.Prepare(fixture.Context, "textures/hud/weather/composite/hail-light.svg");
        element.RenderInteractiveElements(0);
        element.Dispose();

        // Assert
        Assert.True(previous.Disposed);
        Assert.DoesNotContain(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>A successfully drawable SVG is suppressed while a changed asset awaits its layout-time replacement.</summary>
    [Fact]
    public void PreparingAnotherSvgSuppressesThePreviousDrawUntilReplacementLayoutCompletes()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string firstPath = "textures/hud/test/first.svg";
        const string nextPath = "textures/hud/test/next.svg";
        using var element = CreateElement(fixture, firstPath, 0, 0, 12, 14);
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        var previous = Assert.Single(loader.Textures);
        previous.TextureId = 740;
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);
        Assert.Single(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        fixture.Render.Invocations.Clear();
        SetBounds(element, 0, 0, 16, 11);

        // Act
        element.Prepare(fixture.Context, nextPath);
        element.RenderInteractiveElements(0);

        // Assert
        Assert.Single(loader.Requests);
        Assert.DoesNotContain(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.False(previous.Disposed);

        // Act
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));

        // Assert
        Assert.Equal(2, loader.Requests.Count);
        Assert.Equal(new SvgRasterizationRecorder.Request(nextPath, 16, 11, 16, 11), loader.Requests[^1]);
        Assert.True(previous.Disposed);
        var replacement = loader.Textures[^1];
        Assert.Equal(new Size(16, 11), new Size(replacement.Width, replacement.Height));
        replacement.TextureId = 741;
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);
        var draw = Assert.Single(fixture.Render.Invocations
, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Equal(replacement.TextureId, (int)draw.Arguments[0]);
        Assert.Equal(replacement.Width, Convert.ToDouble(draw.Arguments[3]));
        Assert.Equal(replacement.Height, Convert.ToDouble(draw.Arguments[4]));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>An SVG loader cannot transfer borrowed ownership to a drawing element.</summary>
    [Fact]
    public void UnownedSvgBindingIsRejectedWithoutDisposingTheBorrowedTexture()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var borrowed = new LoadedTexture(fixture.Api.Object)
        {
            TextureId = 0,
            Width = 12,
            Height = 14,
            IgnoreUndisposed = true
        };
        var element = new TextureElement(fixture.Api.Object, AbsoluteBounds(0, 0, 12, 14), fixture.Failures.Add,
            (_, _, _) => new(borrowed, false));
        element.Prepare(fixture.Context, "textures/hud/test/borrowed.svg");

        // Act
        var error = Assert.Throws<InvalidOperationException>(() => element.LayoutCompleted(new RectangleF(0, 0, 100, 100)));

        // Assert
        Assert.Contains("owned 12 x 14", error.Message);
        Assert.False(borrowed.Disposed);
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);
        Assert.DoesNotContain(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));

        // Act
        element.Dispose();

        // Assert
        Assert.False(borrowed.Disposed);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Zero-pixel spans are valid, skip rasterization and release any formerly owned SVG.</summary>
    [Fact]
    public void CollapsedPixelSpanDoesNotRasterizeAndReleasesPriorArtwork()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string path = "textures/hud/weather/composite/fog-light.svg";
        using var element = CreateElement(fixture, path, 0, 0, 9, 7);
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        var previous = Assert.Single(loader.Textures);

        // Act
        SetBounds(element, 4.1, 2, .2, 7);
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));

        // Assert
        Assert.True(previous.Disposed);
        Assert.Single(loader.Requests);
        Assert.Equal(0, element.State.Target.Width);
        Assert.False(element.State.Drawable);
        fixture.Render.Invocations.Clear();
        element.RenderInteractiveElements(0);
        Assert.DoesNotContain(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Invalid SVG geometry fails before loading rather than being silently normalized to another canvas.</summary>
    [Theory]
    [InlineData(0d, 0d, -1d, 8d)]
    [InlineData(double.NaN, 0d, 8d, 8d)]
    public void InvalidSvgGeometryThrowsWithoutRasterizing(double x, double y, double width, double height)
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        using var element = CreateElement(fixture, "textures/hud/weather/composite/rain-light.svg", x, y, width, height);

        // Act
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => element.LayoutCompleted(new RectangleF(0, 0, 100, 100)));

        // Assert
        Assert.Equal("Bounds", error.ParamName);
        Assert.Empty(loader.Requests);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>SVGs defer work, while PNG callbacks receive an empty canvas and retain borrowed ownership semantics.</summary>
    [Fact]
    public void SvgDefersButPngLoadsImmediatelyAsBorrowedArtwork()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        var observedSizes = new List<Size>();
        var png = new LoadedTexture(fixture.Api.Object) { TextureId = 0, Width = 37, Height = 43, IgnoreUndisposed = true };
        var element = new TextureElement(fixture.Api.Object, AbsoluteBounds(0, 0, 20, 20), fixture.Failures.Add,
            (context, path, svgSize) =>
            {
                observedSizes.Add(svgSize);
                return path.EndsWith(".svg", StringComparison.Ordinal)
                    ? new(loader.Textures[^1], true)
                    : new(png, false);
            });

        // Act
        element.Prepare(fixture.Context, "textures/hud/test.svg");

        // Assert
        Assert.Empty(observedSizes);
        Assert.Empty(loader.Requests);

        // Act
        element.Prepare(fixture.Context, "textures/hud/test.png");

        // Assert
        Assert.Equal([Size.Empty], observedSizes);
        Assert.True(png.IgnoreUndisposed);
        Assert.False(png.Disposed);

        // Act
        element.Dispose();
        element.Dispose();

        // Assert
        Assert.False(png.Disposed);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Cached PNG artwork draws at fractional layout bounds rather than its native texture dimensions.</summary>
    [Fact]
    public void PngDrawUsesFractionalLayoutBoundsInsteadOfNativeTextureDimensions()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        const string path = "textures/test/fractional-layout.png";
        LoadedTexture? pngTexture = null;
        fixture.Render.Setup(renderer => renderer.GetOrLoadTexture(
                It.IsAny<Vintagestory.API.Common.AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new TextureLoad((Vintagestory.API.Common.AssetLocation _, ref LoadedTexture texture) =>
            {
                texture.Width = 37;
                texture.Height = 43;
                pngTexture = texture;
            }));
        using var element = new TextureElement(fixture.Api.Object,
            AbsoluteBounds(10.25, 14.5, 6.75, 9.25), fixture.Failures.Add);
        element.Prepare(fixture.Context, path);
        var texture = Assert.IsType<LoadedTexture>(pngTexture);

        // Act
        element.LayoutCompleted(new RectangleF(0, 0, 100, 100));
        texture.TextureId = 750;
        element.RenderInteractiveElements(0);

        // Assert
        Assert.Equal(37, texture.Width);
        Assert.Equal(43, texture.Height);
        Assert.Equal(new RectangleF(10.25f, 14.5f, 6.75f, 9.25f), element.State.Target);
        var draw = Assert.Single(fixture.Render.Invocations,
            call => call.Method.Name == nameof(IRenderAPI.RenderTexture));
        Assert.Equal(750, (int)draw.Arguments[0]);
        Assert.Equal(10.25d, Convert.ToDouble(draw.Arguments[1]));
        Assert.Equal(14.5d, Convert.ToDouble(draw.Arguments[2]));
        Assert.Equal(6.75d, Convert.ToDouble(draw.Arguments[3]));
        Assert.Equal(9.25d, Convert.ToDouble(draw.Arguments[4]));
        Assert.Empty(fixture.Failures);
    }

    /// <summary>The default PNG cache remains immediate and borrowed while an earlier generated SVG is released.</summary>
    [Fact]
    public void DefaultPngCacheRemainsBorrowedWhenReplacingSvg()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Gui, fixture.Api.Object);
        const string svgPath = "textures/hud/weather/composite/rain-light.svg";
        const string pngPath = "textures/test/borrowed.png";
        LoadedTexture? pngTexture = null;
        var pngPaths = new List<string>();
        fixture.Render.Setup(renderer => renderer.GetOrLoadTexture(
                It.IsAny<Vintagestory.API.Common.AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new TextureLoad((Vintagestory.API.Common.AssetLocation location, ref LoadedTexture texture) =>
            {
                pngPaths.Add(location.Path);
                texture.Width = 31;
                texture.Height = 37;
                pngTexture = texture;
            }));
        using var element = new TextureElement(fixture.Api.Object, AbsoluteBounds(0, 0, 13, 9), fixture.Failures.Add);

        // Act
        element.Prepare(fixture.Context, svgPath);
        Assert.Empty(loader.Requests);
        element.LayoutCompleted(new RectangleF(0, 0, 50, 50));
        var svgTexture = Assert.Single(loader.Textures);
        element.LayoutCompleted(new RectangleF(0, 0, 50, 50));

        // Assert
        Assert.Single(loader.Requests);
        Assert.False(svgTexture.Disposed);

        // Act
        element.Prepare(fixture.Context, pngPath);

        // Assert
        Assert.True(svgTexture.Disposed);
        Assert.Equal([pngPath], pngPaths);
        Assert.NotNull(pngTexture);
        Assert.True(pngTexture!.IgnoreUndisposed);

        // Act
        element.LayoutCompleted(new RectangleF(0, 0, 50, 50));
        element.Dispose();
        element.Dispose();

        // Assert
        Assert.False(pngTexture.Disposed);
        fixture.Gui.Verify(gui => gui.LoadSvg(
            It.Is<Vintagestory.API.Common.AssetLocation>(location => location.Path == svgPath),
            13, 9, 13, 9, It.IsAny<int?>()), Times.Once);
        fixture.Render.Verify(renderer => renderer.GetOrLoadTexture(
            It.Is<Vintagestory.API.Common.AssetLocation>(location => location.Path == pngPath),
            ref It.Ref<LoadedTexture>.IsAny), Times.Once);
        Assert.Empty(fixture.Failures);
    }
    #endregion

    #region Private
    private static TextureElement CreateElement(HudNativeComponentFixture fixture, string path,
        double x, double y, double width, double height)
    {
        var element = new TextureElement(fixture.Api.Object, AbsoluteBounds(x, y, width, height), fixture.Failures.Add);
        element.Prepare(fixture.Context, path);
        return element;
    }

    private static ElementBounds AbsoluteBounds(double x, double y, double width, double height)
    {
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
        bounds.absFixedX = x;
        bounds.absFixedY = y;
        bounds.absInnerWidth = width;
        bounds.absInnerHeight = height;
        bounds.Initialized = true;
        return bounds;
    }

    private static void SetBounds(TextureElement element, double x, double y, double width, double height)
    {
        element.Bounds.absFixedX = x;
        element.Bounds.absFixedY = y;
        element.Bounds.absInnerWidth = width;
        element.Bounds.absInnerHeight = height;
        element.Bounds.Initialized = true;
    }

    private delegate void TextureLoad(Vintagestory.API.Common.AssetLocation location, ref LoadedTexture texture);
    #endregion
}
