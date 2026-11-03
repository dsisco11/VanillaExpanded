using System.Drawing;
using Moq;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.PlayerWetness;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies native layered status composition, resource ownership and prepared effect geometry.</summary>
[Collection("HudOverlayGeometry")]
public sealed class LayeredIndicatorsTests
{
    #region Public API
    #region Registration, cache and ownership
    /// <summary>All three consumers and both styles register real native components rather than manual drawing callbacks.</summary>
    [Theory]
    [InlineData("wetness", HudIndicatorStyle.Icon, 1)] [InlineData("wetness", HudIndicatorStyle.Text, 2)]
    [InlineData("body", HudIndicatorStyle.Icon, 2)] [InlineData("body", HudIndicatorStyle.Text, 1)]
    [InlineData("weather", HudIndicatorStyle.Icon, 1)] [InlineData("weather", HudIndicatorStyle.Text, 2)]
    public void SupportedStylesUseNativeComponentsAndPassiveInput(string kind, HudIndicatorStyle style, float scale)
    {
        using var fixture = new LayeredIndicatorsFixture(scale);
        var (overlay, presentation, _, _) = fixture.Create(kind, style);
        using (overlay)
        {
            Assert.Empty(fixture.Bindings); Assert.Empty(fixture.Glyphs);
            using var view = ViewFactory.Create(overlay, fixture.Resources.Native.Api.Object, NativeViewRole.Live, fixture.Resources.Native.Failures.Add);
            using var composition = new IndicatorComposition(fixture.Resources.Native.Api.Object, view, fixture.Context, presentation.Size);
            var elements = Components(composition, kind, style);
            if (style == HudIndicatorStyle.Text || kind != "weather")
            {
                var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
                Assert.Equal(style == HudIndicatorStyle.Icon ? EnumTextOrientation.Center : EnumTextOrientation.Left, reading.Font.Orientation);
            }
            Assert.All(elements, element =>
            {
                Assert.True(element is TextElement or TextureElement);
                Assert.False(element.Focusable);
                var press = new MouseEvent(0, 0, EnumMouseButton.Left, 0);
                element.OnMouseDownOnElement(fixture.Resources.Native.Api.Object, press); Assert.False(press.Handled);
            });
            if (style == HudIndicatorStyle.Text)
            {
                var name = Assert.IsType<TextElement>(composition.Composer.GetElement("test:name"));
                var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
                Assert.True(name.State.Target.Bottom <= reading.State.Target.Top + .001);
                Assert.Empty(fixture.Bindings); Assert.Equal(0, fixture.PackChecks);
            }
            composition.Composer.Render(.1f);
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    /// <summary>Repeated foreground/backdrop rendering never measures, prepares resources or resolves dirty component bounds.</summary>
    [Theory]
    [InlineData("wetness", HudIndicatorStyle.Icon)] [InlineData("wetness", HudIndicatorStyle.Text)]
    [InlineData("body", HudIndicatorStyle.Icon)] [InlineData("body", HudIndicatorStyle.Text)]
    [InlineData("weather", HudIndicatorStyle.Icon)] [InlineData("weather", HudIndicatorStyle.Text)]
    public void RenderingConsumesOnlyCachedResourcesAndGeometry(string kind, HudIndicatorStyle style)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, style);
        using (overlay)
        using (var composition = Mount(fixture, overlay, presentation))
        {
            var element = Components(composition, kind, style)[0];
            var state = State(element); var target = state.Target;
            int measurements = fixture.Measurements, glyphs = fixture.Glyphs.Count, textures = fixture.LoadRequests.Count, checks = fixture.PackChecks;
            element.Bounds.fixedWidth += 77; element.Bounds.MarkDirtyRecursive();
            for (int frame = 0; frame < 4; frame++)
            {
                ((INativePresentation)overlay).BeginFrame(.1f); composition.View.SetOpacity(.6f);
                composition.View.CollectBackdrops(fixture.Resources.Native.Render.Object); composition.Composer.Render(.1f);
            }
            Assert.Equal(target, state.Target); Assert.False(element.Bounds.Initialized);
            Assert.Equal(measurements, fixture.Measurements); Assert.Equal(glyphs, fixture.Glyphs.Count);
            Assert.Equal(textures, fixture.LoadRequests.Count); Assert.Equal(checks, fixture.PackChecks);
        }
    }
    /// <summary>Separate compositions own separate SVG rasters and release their resources when retired.</summary>
    [Theory]
    [InlineData("wetness", HudIndicatorStyle.Icon)] [InlineData("wetness", HudIndicatorStyle.Text)]
    [InlineData("body", HudIndicatorStyle.Icon)] [InlineData("body", HudIndicatorStyle.Text)]
    [InlineData("weather", HudIndicatorStyle.Icon)] [InlineData("weather", HudIndicatorStyle.Text)]
    public void IndependentViewsOwnGlyphsAndSvgRastersAcrossUpdatesAndStyles(string kind, HudIndicatorStyle style)
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        {
            // Act
            using var live = Mount(fixture, overlay, presentation);
            int liveCount = fixture.Glyphs.Count;
            var liveRasters = fixture.SvgTextures.Select(entry => entry.Texture).ToArray();
            using var source = Mount(fixture, overlay, presentation, NativeViewRole.SourceTrace);
            var sourceGlyphs = fixture.Glyphs.Skip(liveCount).ToArray();
            var sourceRasters = fixture.SvgTextures.Skip(liveRasters.Length).Select(entry => entry.Texture).ToArray();

            // Assert
            Assert.NotSame(Components(live, kind, style)[0], Components(source, kind, style)[0]);
            Assert.All(liveRasters, texture => Assert.DoesNotContain(texture, sourceRasters));

            // Act
            change(); prepare(); live.Update(fixture.Context);
            int beforeSource = fixture.Glyphs.Count; source.Update(fixture.Context);
            var survivingGlyphs = sourceGlyphs.Concat(fixture.Glyphs.Skip(beforeSource)).Where(glyph => !glyph.Disposed).ToArray();
            live.Dispose();
            source.View.SetOpacity(.5f); source.Composer.Render(0);

            // Assert
            if (kind != "weather" || style == HudIndicatorStyle.Text) Assert.NotEmpty(survivingGlyphs);
            Assert.All(survivingGlyphs, glyph => Assert.False(glyph.Disposed));

            // Act
            var alternate = style == HudIndicatorStyle.Icon ? HudIndicatorStyle.Text : HudIndicatorStyle.Icon;
            presentation.SetStyle(alternate); prepare();
            using var replacement = Mount(fixture, overlay, presentation);
            source.Dispose(); replacement.Composer.Render(0);
            replacement.Dispose();

            // Assert
            Assert.All(fixture.Glyphs, glyph => Assert.True(glyph.Disposed));
            Assert.All(fixture.SvgTextures, raster => Assert.Equal(
                new Size(raster.SvgSize.Width, raster.SvgSize.Height),
                new Size(raster.Texture.Width, raster.Texture.Height)));
            Assert.All(fixture.SvgTextures, raster => Assert.True(raster.Texture.Disposed));
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    /// <summary>Movement reuses rasters, while GUI-scale changes replace SVGs at their new native dimensions.</summary>
    [Theory]
    [InlineData("wetness")] [InlineData("body")] [InlineData("weather")]
    public void MovementAndScaleInvalidateOnlyRequiredWork(string kind)
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create(kind, HudIndicatorStyle.Icon);
        using (overlay)
        using (var composition = Mount(fixture, overlay, presentation))
        {
            var element = Components(composition, kind, HudIndicatorStyle.Icon)[0];
            var initial = State(element).Target;
            int textures = fixture.LoadRequests.Count, glyphs = fixture.Glyphs.Count;

            // Act
            composition.Composer.Bounds.fixedX += 17; composition.Composer.Bounds.fixedY += 23;
            composition.Composer.Bounds.MarkDirtyRecursive(); composition.Composer.Bounds.CalcWorldBounds();
            composition.View.LayoutCompleted(new(0, 0, 1800, 1800));

            // Assert
            Assert.Equal(initial.X + 17, State(element).Target.X, 4); Assert.Equal(initial.Y + 23, State(element).Target.Y, 4);
            Assert.Equal(glyphs, fixture.Glyphs.Count); Assert.Equal(textures, fixture.LoadRequests.Count);

            // Act
            var beforeScale = State(element).Target;
            Vintagestory.API.Config.RuntimeEnv.GUIScale = 2; prepare(); composition.Update(fixture.Context);

            // Assert
            Assert.Equal(beforeScale.Width * 2, State(element).Target.Width, 3);
            Assert.True(fixture.LoadRequests.Count > textures);
            Assert.All(fixture.SvgTextures, raster => Assert.Equal(
                new Size(raster.SvgSize.Width, raster.SvgSize.Height),
                new Size(raster.Texture.Width, raster.Texture.Height)));
            int afterScale = fixture.LoadRequests.Count;
            if (glyphs > 0) Assert.All(fixture.Glyphs.Take(glyphs), glyph => Assert.True(glyph.Disposed));

            // Act
            int beforeContext = fixture.Glyphs.Count;
            composition.Update(new(fixture.Context.Api, 2, "fr", 1));

            // Assert
            if (glyphs > 0) Assert.True(fixture.Glyphs.Count > beforeContext);
            Assert.Equal(afterScale, fixture.LoadRequests.Count);
        }
    }
    /// <summary>Weather fallback SVGs use the default generated-texture loader with per-composition reuse and ownership.</summary>
    [Fact]
    public void WeatherFallbackSvgLoaderOwnsIndependentResourcesAcrossUpdates()
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Resources.Native.Gui, fixture.Context.Api);
        var requests = loader.Requests;
        var svgTextures = loader.Textures;
        using var presentation = new WeatherConditionsPresentation(loadBackdrop: _ => null, hasCompletePack: _ => false);
        presentation.SetContent(new(EnumPrecipitationType.Rain, true));
        presentation.Prepare(fixture.Context);

        // Act
        var firstView = (NativeView)presentation.CreateView(fixture.Context.Api);
        var secondView = (NativeView)presentation.CreateView(fixture.Context.Api);
        firstView.SetFailureHandler(fixture.Resources.Native.Failures.Add);
        secondView.SetFailureHandler(fixture.Resources.Native.Failures.Add);
        using var first = new IndicatorComposition(fixture.Context.Api, firstView, fixture.Context, presentation.Size);
        using var second = new IndicatorComposition(fixture.Context.Api, secondView, fixture.Context, presentation.Size);

        // Assert
        string[] initialPaths =
        [
            "textures/hud/weather/rain.svg", "textures/hud/weather/fog.svg",
            "textures/hud/weather/rain.svg", "textures/hud/weather/fog.svg"
        ];
        Assert.Equal(initialPaths, requests.Select(request => request.Path));
        Assert.Equal(4, svgTextures.Count);
        Assert.Equal(4, svgTextures.Distinct().Count());
        Assert.All(requests, request =>
        {
            Assert.True(request.Width > 0);
            Assert.True(request.Height > 0);
            Assert.Equal(request.Width, request.TextureWidth);
            Assert.Equal(request.Height, request.TextureHeight);
        });
        Assert.Equal(requests.Select(request => (request.Width, request.Height)),
            svgTextures.Select(texture => (texture.Width, texture.Height)));
        Assert.Single(requests.Select(request => (request.Width, request.Height)).Distinct());
        var firstRain = svgTextures[0];
        var firstFog = svgTextures[1];
        var secondRain = svgTextures[2];
        var secondFog = svgTextures[3];

        // Act
        firstView.Prepare(fixture.Context);
        secondView.Prepare(fixture.Context);

        // Assert
        Assert.Equal(initialPaths, requests.Select(request => request.Path));
        Assert.All(svgTextures, texture => Assert.False(texture.Disposed));

        // Assign synthetic IDs after filtering so draw routing can be observed without graphics initialization.
        for (int index = 0; index < svgTextures.Count; index++) svgTextures[index].TextureId = 700 + index;
        fixture.Resources.Native.Render.Invocations.Clear();
        first.Composer.Render(0);
        var initialDraws = fixture.Resources.Native.Render.Invocations
            .Where(call => call.Method.Name == nameof(IRenderAPI.RenderTexture)).ToArray();
        Assert.Equal(new[] { firstRain.TextureId, firstFog.TextureId }, initialDraws.Select(call => (int)call.Arguments[0]));
        Assert.Equal(new[] { (double)firstRain.Width, firstFog.Width },
            initialDraws.Select(call => Convert.ToDouble(call.Arguments[3])));
        Assert.Equal(new[] { (double)firstRain.Height, firstFog.Height },
            initialDraws.Select(call => Convert.ToDouble(call.Arguments[4])));
        Assert.Empty(fixture.Resources.Native.Failures);

        // Act
        presentation.SetContent(new(EnumPrecipitationType.Snow, true));
        presentation.Prepare(fixture.Context);
        first.Update(fixture.Context);

        // Assert
        Assert.True(firstRain.Disposed);
        Assert.False(firstFog.Disposed);
        Assert.False(secondRain.Disposed);
        Assert.False(secondFog.Disposed);
        Assert.Equal("textures/hud/weather/snow.svg", requests[^1].Path);
        var firstSnow = Assert.IsType<LoadedTexture>(svgTextures[^1]);
        Assert.False(firstSnow.Disposed);

        // Act
        second.Update(fixture.Context);

        // Assert
        Assert.True(secondRain.Disposed);
        Assert.False(secondFog.Disposed);
        Assert.Equal("textures/hud/weather/snow.svg", requests[^1].Path);
        var secondSnow = Assert.IsType<LoadedTexture>(svgTextures[^1]);
        Assert.NotSame(firstSnow, secondSnow);

        // Act
        first.Dispose();

        // Assert
        Assert.True(firstSnow.Disposed);
        Assert.True(firstFog.Disposed);
        Assert.False(secondSnow.Disposed);
        Assert.False(secondFog.Disposed);
        second.Dispose();
        Assert.All(svgTextures, texture => Assert.True(texture.Disposed));
        Assert.Empty(fixture.Resources.Native.Failures);
    }
    #endregion
    #region Wetness and thermometer geometry
    /// <summary>Wetness percentage changes leave both fully filled silhouettes and their aligned outlines unchanged.</summary>
    [Theory]
    [InlineData(.001f, "1%")] [InlineData(.5f, "50%")] [InlineData(1, "100%")]
    public void WetnessReadingsFitAboveStableFullDrops(float wetness, string expected)
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture(2) { TextSize = new(140, 32) };
        using var content = new PlayerWetnessPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => fixture.Resources.Backdrop.Object);

        // Act
        content.SetContent(wetness); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);

        // Assert
        Assert.Contains(expected, fixture.Values);
        var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
        var drops = Enumerable.Range(0, 4).Select(index => Assert.IsType<TextureElement>(composition.Composer.GetElement("test:drop-" + index))).ToArray();
        Assert.Equal(drops[0].State.Target, drops[1].State.Target); Assert.Equal(drops[2].State.Target, drops[3].State.Target);
        Assert.Equal(drops[0].Bounds.ParentBounds.OuterWidth, drops[0].Bounds.ParentBounds.OuterHeight);
        Assert.True(drops[0].State.Target.Width > drops[2].State.Target.Width);
        Assert.Equal(drops[0].State.Target.Width, drops[0].State.Target.Height);
        Assert.Equal(drops[2].State.Target.Width, drops[2].State.Target.Height);
        Assert.NotNull(drops[0].State.Tint(false));
        Assert.Null(drops[1].State.Tint(false));
        Assert.NotNull(drops[2].State.Tint(false));
        Assert.Null(drops[3].State.Tint(false));
        string[] expectedAssets =
        [
            "textures/hud/player-wetness/fill-mask.svg",
            "textures/hud/player-wetness/outline.svg"
        ];
        Assert.Equal(expectedAssets.OrderBy(path => path), fixture.Bindings.Keys.OrderBy(path => path));

        // Act
        composition.View.CollectBackdrops(fixture.Context.Api.Render);

        // Assert
        var backdrop = Assert.Single(fixture.Resources.Backdrop.Invocations);
        Assert.Equal(drops[0].Bounds.ParentBounds.renderX, Assert.IsType<double>(backdrop.Arguments[1]), 4);
        Assert.Equal(drops[0].Bounds.ParentBounds.renderY, Assert.IsType<double>(backdrop.Arguments[2]), 4);
        Assert.Equal(drops[0].Bounds.ParentBounds.OuterWidth / HudOverlaySizes.Standard,
            Assert.IsType<double>(backdrop.Arguments[3]), 6);
        Assert.True(reading.State.Target.Bottom <= drops.Min(drop => drop.State.Target.Top) + .001);
        Assert.Equal(reading.Bounds.renderX + reading.Bounds.OuterWidth / 2,
            drops[0].Bounds.ParentBounds.renderX + drops[0].Bounds.ParentBounds.OuterWidth / 2, 4);

        // Act
        composition.View.SetOpacity(0);
        fixture.Resources.Native.Render.Invocations.Clear();
        composition.Composer.Render(0);

        // Assert
        Assert.Empty(fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "Render2DTexture"));

        // Act
        composition.View.SetOpacity(1);
        var targets = drops.Select(drop => drop.State.Target).ToArray();

        // Act
        content.SetContent(.26f); content.Prepare(fixture.Context); composition.Update(fixture.Context);

        // Assert
        Assert.Equal(targets, drops.Select(drop => drop.State.Target));
        var artwork = fixture.SvgTextures.Select(raster => raster.Texture).ToArray();
        Assert.Equal(4, artwork.Length);
        for (int index = 0; index < artwork.Length; index++) artwork[index].TextureId = 2000 + index;
        fixture.Resources.Native.Render.Invocations.Clear();
        composition.Composer.Render(0);
        var draws = fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "Render2DTexture").ToArray();
        Assert.Equal(4, draws.Length);
        Assert.Equal(artwork.Select(texture => texture.TextureId), draws.Select(draw => (int)draw.Arguments[0]));
        Assert.Equal(artwork.Select(texture => (double)texture.Width), draws.Select(draw => Convert.ToDouble(draw.Arguments[3])));
        Assert.Equal(artwork.Select(texture => (double)texture.Height), draws.Select(draw => Convert.ToDouble(draw.Arguments[4])));
        Assert.Equal((artwork[0].Width, artwork[0].Height), (artwork[1].Width, artwork[1].Height));
        Assert.Equal((artwork[2].Width, artwork[2].Height), (artwork[3].Width, artwork[3].Height));
        Assert.True(artwork[0].Width > artwork[2].Width);
        Assert.NotNull(draws[0].Arguments[6]);
        Assert.Null(draws[1].Arguments[6]);
        Assert.NotNull(draws[2].Arguments[6]);
        Assert.Null(draws[3].Arguments[6]);
    }

    /// <summary>Wetness fill and outline layers use independent default SVG resources, reuse unchanged paths, and retire per composition.</summary>
    [Fact]
    public void WetnessSvgLayersReuseDefaultLoadsAndRetainIndependentCompositionOwnership()
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Resources.Native.Gui, fixture.Context.Api);
        var requests = loader.Requests;
        var svgTextures = loader.Textures;
        using var content = new PlayerWetnessPresentation(fixture.Measure, fixture.CreateText, loadBackdrop: _ => null);
        content.SetContent(.5f);
        content.Prepare(fixture.Context);
        string[] expectedPaths =
        [
            "textures/hud/player-wetness/fill-mask.svg",
            "textures/hud/player-wetness/outline.svg",
            "textures/hud/player-wetness/fill-mask.svg",
            "textures/hud/player-wetness/outline.svg"
        ];

        // Act
        using var first = new IndicatorComposition(
            fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);

        // Assert
        Assert.Equal(expectedPaths, requests.Select(request => request.Path));
        Assert.Equal(4, svgTextures.Count);
        Assert.Equal(4, svgTextures.Distinct().Count());
        Assert.All(requests, request =>
        {
            Assert.True(request.Width > 0);
            Assert.True(request.Height > 0);
            Assert.Equal(request.Width, request.TextureWidth);
            Assert.Equal(request.Height, request.TextureHeight);
        });
        Assert.Equal(requests.Select(request => (request.Width, request.Height)),
            svgTextures.Select(texture => (texture.Width, texture.Height)));
        fixture.Resources.Native.Render.Verify(
            renderer => renderer.GetOrLoadTexture(It.IsAny<AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny),
            Times.Never);
        var firstTextures = svgTextures.ToArray();
        Assert.All(firstTextures, texture => Assert.False(texture.Disposed));

        // Act
        content.Prepare(fixture.Context);
        first.Update(fixture.Context);
        content.SetContent(.26f);
        content.Prepare(fixture.Context);
        first.Update(fixture.Context);

        // Assert
        Assert.Equal(4, requests.Count);
        Assert.All(firstTextures, texture => Assert.False(texture.Disposed));

        // Act
        var secondView = content.CreateView(fixture.Context.Api);
        ((NativeView)secondView).SetFailureHandler(fixture.Resources.Native.Failures.Add);
        using var second = new IndicatorComposition(
            fixture.Context.Api, secondView, fixture.Context, content.Size);

        // Assert
        Assert.Equal(expectedPaths.Concat(expectedPaths), requests.Select(request => request.Path));
        Assert.Equal(8, svgTextures.Count);
        var secondTextures = svgTextures.Skip(4).ToArray();
        Assert.Equal(4, secondTextures.Distinct().Count());
        Assert.All(secondTextures, texture => Assert.False(texture.Disposed));

        // Act
        // Keep default-loader filtering headless with zero-ID wrappers; add synthetic draw identities only afterward.
        for (int index = 0; index < secondTextures.Length; index++)
            secondTextures[index].TextureId = 3000 + index;

        // Act
        first.Dispose();

        // Assert
        Assert.All(firstTextures, texture => Assert.True(texture.Disposed));
        Assert.All(secondTextures, texture => Assert.False(texture.Disposed));

        // Act
        fixture.Resources.Native.Render.Invocations.Clear();
        second.Composer.Render(0);
        var draws = fixture.Resources.Native.Render.Invocations
            .Where(call => call.Method.Name == "Render2DTexture").ToArray();

        // Assert
        Assert.Equal(4, draws.Length);
        var secondTextureIds = secondTextures.Select(texture => texture.TextureId).ToArray();
        Assert.All(secondTextureIds, textureId => Assert.True(textureId > 0));
        Assert.Equal(secondTextureIds.Length, secondTextureIds.Distinct().Count());
        Assert.Equal(secondTextureIds, draws.Select(draw => (int)draw.Arguments[0]));
        Assert.Equal(secondTextures.Select(texture => (double)texture.Width),
            draws.Select(draw => Convert.ToDouble(draw.Arguments[3])));
        Assert.Equal(secondTextures.Select(texture => (double)texture.Height),
            draws.Select(draw => Convert.ToDouble(draw.Arguments[4])));
        Assert.Empty(fixture.Resources.Native.Failures);

        // Act
        second.Dispose();

        // Assert
        Assert.All(secondTextures, texture => Assert.True(texture.Disposed));
    }
    /// <summary>Optional readings and both marker strengths preserve direction, containment and stable icon-slot alignment.</summary>
    [Theory]
    [InlineData(-2, true, 1)] [InlineData(-1, true, 2)] [InlineData(0, true, 1)]
    [InlineData(1, true, 2)] [InlineData(2, true, 1)]
    [InlineData(-2, false, 2)] [InlineData(-1, false, 1)] [InlineData(0, false, 2)]
    [InlineData(1, false, 1)] [InlineData(2, false, 2)]
    public void BodyReadingAndMarkersUseNativeBounds(int direction, bool showReading, float scale)
    {
        using var fixture = new LayeredIndicatorsFixture(scale) { TextSize = new(100, 27) };
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => fixture.Resources.Backdrop.Object);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, direction), showReading); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
        var outline = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:outline"));
        var fill = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:fill"));
        Assert.Equal(outline.State.Target, fill.State.Target);
        Assert.Equal(24 * scale, outline.Bounds.ParentBounds.OuterWidth);
        Assert.Equal(24 * scale, outline.Bounds.ParentBounds.OuterHeight);
        Assert.Equal(14 * scale, outline.State.Target.Width);
        Assert.Equal(24 * scale, outline.State.Target.Height);
        composition.View.CollectBackdrops(fixture.Context.Api.Render);
        var backdrop = Assert.Single(fixture.Resources.Backdrop.Invocations);
        Assert.Equal(outline.Bounds.ParentBounds.renderX, Assert.IsType<double>(backdrop.Arguments[1]), 4);
        Assert.Equal(outline.Bounds.ParentBounds.renderY, Assert.IsType<double>(backdrop.Arguments[2]), 4);
        Assert.Equal((double)scale * 24 / 36, Assert.IsType<double>(backdrop.Arguments[3]), 6);
        Assert.True(outline.Bounds.OuterWidth < outline.Bounds.ParentBounds.OuterWidth);
        Assert.Equal(outline.Bounds.renderX + outline.Bounds.OuterWidth / 2, outline.Bounds.ParentBounds.renderX + outline.Bounds.ParentBounds.OuterWidth / 2, 4);
        if (showReading) Assert.True(reading.State.Target.Bottom <= outline.State.Target.Top + .001);
        else Assert.Equal(0, reading.State.Target.Height);
        for (int index = 0; index < 2; index++)
        {
            var marker = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:trend-" + index));
            if (index >= Math.Abs(direction)) { Assert.Equal(0, marker.Bounds.OuterHeight); continue; }
            Assert.True(marker.State.Target.Bottom <= outline.State.Target.Top + .001);
            if (showReading && direction > 0) Assert.True(marker.State.Target.Bottom <= reading.State.Target.Top + .001);
            if (showReading && direction < 0) Assert.True(marker.State.Target.Top >= reading.State.Target.Bottom - .001);
            if (index > 0)
            {
                var previous = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:trend-" + (index - 1)));
                Assert.True(previous.State.Target.Bottom <= marker.State.Target.Top);
            }
        }
        Assert.Equal(showReading ? 2 : 0, fixture.Glyphs.Count);
        if (showReading)
        {
            Assert.True(fixture.Glyphs[0].Disposed);
            Assert.True(reading.Font.UnscaledFontsize < 14);
            Assert.Equal(new SizeF(fixture.Glyphs[^1].Width, fixture.Glyphs[^1].Height), reading.State.Target.Size);
        }
        Assert.Equal(Math.Abs(direction) == 0 ? 2 : 3, fixture.Bindings.Count);
        string[] expectedAssets =
        [
            "textures/hud/body-temperature/fill-mask.svg",
            "textures/hud/body-temperature/outline.svg"
        ];
        if (direction != 0)
            expectedAssets = [.. expectedAssets, $"textures/hud/body-temperature/chevron-{(direction > 0 ? "up" : "down")}.svg"];
        Assert.Equal(expectedAssets.OrderBy(path => path), fixture.Bindings.Keys.OrderBy(path => path));
    }
    /// <summary>The thermometer uses its default SVG loader for selected non-square assets and releases generated textures.</summary>
    [Theory]
    [InlineData(-2, "down")]
    [InlineData(-1, "down")]
    [InlineData(0, null)]
    [InlineData(1, "up")]
    [InlineData(2, "up")]
    public void ThermometerViewLoadsSelectedSvgAssetsThroughTheDefaultLoaderAndReleasesTextures(int direction, string? chevron)
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Resources.Native.Gui, fixture.Context.Api);
        var requests = loader.Requests;
        var svgTextures = loader.Textures;
        using var content = new BodyTemperatureThermometerPresentation(
            measure: fixture.Measure, createText: fixture.CreateText, loadBackdrop: _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, direction), true);
        content.Prepare(fixture.Context);

        // Act
        using var composition = new IndicatorComposition(
            fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);

        // Assert
        var layeredAssets = requests.Where(request =>
            request.Path.EndsWith("/fill-mask.svg", StringComparison.Ordinal)
            || request.Path.EndsWith("/outline.svg", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, layeredAssets.Length);
        string[] expectedLayerPaths =
        {
            "textures/hud/body-temperature/fill-mask.svg",
            "textures/hud/body-temperature/outline.svg"
        };
        Assert.Equal(expectedLayerPaths.OrderBy(path => path), layeredAssets.Select(request => request.Path).OrderBy(path => path));
        var layeredCanvas = Assert.Single(layeredAssets.Select(request => (request.Width, request.Height)).Distinct());
        Assert.NotEqual(layeredCanvas.Width, layeredCanvas.Height);

        var markerRequests = requests.Where(request => request.Path.Contains("/chevron-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(Math.Abs(direction), markerRequests.Length);
        Assert.All(markerRequests, request =>
        {
            Assert.EndsWith($"/chevron-{chevron}.svg", request.Path);
        });
        if (markerRequests.Length > 0)
        {
            var markerCanvas = Assert.Single(markerRequests.Select(request => (request.Width, request.Height)).Distinct());
            Assert.NotEqual(markerCanvas, layeredCanvas);
            Assert.NotEqual(markerCanvas.Width, markerCanvas.Height);
        }

        var expectedPaths = new List<string>(expectedLayerPaths);
        if (chevron is not null)
            expectedPaths.AddRange(Enumerable.Repeat(
                $"textures/hud/body-temperature/chevron-{chevron}.svg", Math.Abs(direction)));
        Assert.Equal(expectedPaths.OrderBy(path => path), requests.Select(request => request.Path).OrderBy(path => path));
        Assert.All(requests, request =>
        {
            Assert.Equal(request.Width, request.TextureWidth);
            Assert.Equal(request.Height, request.TextureHeight);
        });
        Assert.Equal(requests.Select(request => (request.Width, request.Height)),
            svgTextures.Select(texture => (texture.Width, texture.Height)));
        Assert.All(svgTextures, texture => Assert.False(texture.Disposed));
        for (int index = 0; index < svgTextures.Count; index++)
            svgTextures[index].TextureId = 4000 + index;
        fixture.Resources.Native.Render.Invocations.Clear();
        composition.Composer.Render(0);
        var draws = fixture.Resources.Native.Render.Invocations
            .Where(call => call.Method.Name == nameof(IRenderAPI.RenderTexture)).ToArray();
        Assert.Equal(svgTextures.Count, draws.Length);
        foreach (var draw in draws)
        {
            var texture = Assert.Single(svgTextures.Where(value => value.TextureId == (int)draw.Arguments[0]));
            Assert.Equal(texture.Width, Convert.ToDouble(draw.Arguments[3]));
            Assert.Equal(texture.Height, Convert.ToDouble(draw.Arguments[4]));
        }
        composition.Dispose();
        Assert.All(svgTextures, texture => Assert.True(texture.Disposed));
        Assert.Empty(fixture.Resources.Native.Failures);
    }

    /// <summary>Live indicator and GUI scale changes replace the host's default-loaded SVGs at independently resolved native pixels.</summary>
    [Fact]
    public void ThermometerHostRerasterizesExactNativePixelsAfterLiveScaleChanges()
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        int configuredSize = 24;
        var sizeSetting = new HudOverlaySizeSetting(24, () => configuredSize, value => configuredSize = value, () => { });
        using var host = new HudNativeHostFixture(new HudOverlayMenuSettings(Size: sizeSetting));
        var loader = new SvgRasterizationRecorder();
        loader.Setup(host.Components.Gui, host.Components.Api.Object);
        using var content = new BodyTemperatureThermometerPresentation(
            measure: fixture.Measure, createText: fixture.CreateText, loadBackdrop: _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), true);
        content.Prepare(new(host.Components.Api.Object, Vintagestory.API.Config.RuntimeEnv.GUIScale, "en"));
        host.Native.Setup(native => native.CreateView(host.Components.Api.Object, It.IsAny<NativeViewRole>()))
            .Returns((ICoreClientAPI api, NativeViewRole _) => content.CreateView(api));
        host.Size = sizeSetting.Apply(content.Size);

        // Act
        host.Synchronize();

        // Assert
        var initial = AssertHostThermometerRasters(host, loader, 8100);
        Assert.Equal(2, loader.Requests.Count);
        var initialTextures = initial.Select(item => item.Texture).ToArray();

        // Act
        sizeSetting.Set(36);
        content.Prepare(new(host.Components.Api.Object, Vintagestory.API.Config.RuntimeEnv.GUIScale, "en"));
        host.Size = sizeSetting.Apply(content.Size);
        host.Synchronize();

        // Assert
        Assert.All(initialTextures, texture => Assert.True(texture.Disposed));
        Assert.Equal(4, loader.Requests.Count);
        var resized = AssertHostThermometerRasters(host, loader, 8200);
        Assert.All(initial.Zip(resized), pair =>
        {
            Assert.Equal(pair.First.Path, pair.Second.Path);
            Assert.DoesNotContain(pair.Second.Texture, initialTextures);
            Assert.NotEqual(new Size(pair.First.Texture.Width, pair.First.Texture.Height),
                new Size(pair.Second.Texture.Width, pair.Second.Texture.Height));
        });
        var resizedTextures = resized.Select(item => item.Texture).ToArray();

        // Act
        Vintagestory.API.Config.RuntimeEnv.GUIScale = 1.25f;
        content.Prepare(new(host.Components.Api.Object, Vintagestory.API.Config.RuntimeEnv.GUIScale, "en"));
        host.Size = sizeSetting.Apply(content.Size);
        host.Synchronize();

        // Assert
        Assert.All(resizedTextures, texture => Assert.True(texture.Disposed));
        Assert.Equal(6, loader.Requests.Count);
        var fractionalScale = AssertHostThermometerRasters(host, loader, 8300);
        Assert.All(resized.Zip(fractionalScale), pair =>
        {
            Assert.Equal(pair.First.Path, pair.Second.Path);
            Assert.NotEqual(new Size(pair.First.Texture.Width, pair.First.Texture.Height),
                new Size(pair.Second.Texture.Width, pair.Second.Texture.Height));
        });
        var fractionalTextures = fractionalScale.Select(item => item.Texture).ToArray();

        // Act
        host.Synchronize();

        // Assert
        Assert.Equal(6, loader.Requests.Count);
        var unchanged = AssertHostThermometerRasters(host, loader, 8400);
        Assert.Equal(fractionalTextures, unchanged.Select(item => item.Texture));
        Assert.All(fractionalTextures, texture => Assert.False(texture.Disposed));
        Assert.Empty(host.Failures);
    }

    /// <summary>Fill-only updates refresh the prepared reveal without dirtying native bounds or regenerating resources.</summary>
    [Fact]
    public void ThermometerFillChangesClipWithoutLayoutOrGlyphChanges()
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), true); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        var fill = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:fill"));
        var target = fill.State.Target; int glyphs = fixture.Glyphs.Count, textures = fixture.LoadRequests.Count;
        var clips = new List<RectangleF>();
        var colors = new List<(float Red, float Green, float Blue, float Alpha)>();
        fixture.Resources.Native.Render.Setup(renderer => renderer.PushScissor(It.IsAny<ElementBounds>(), true))
            .Callback<ElementBounds, bool>((bounds, _) => clips.Add(Rect(bounds)));
        fixture.Resources.Native.Render.Setup(renderer => renderer.RenderTexture(It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback<int, double, double, double, double, float, Vec4f>((_, _, _, _, _, _, tint) => colors.Add((tint.X, tint.Y, tint.Z, tint.W)));
        // Act
        fill.RenderInteractiveElements(0);
        content.SetContent(new(34.01f, 37, BodyTemperatureRisk.Freezing, .75f), true); content.Prepare(fixture.Context);
        composition.View.Prepare(fixture.Context);
        Assert.True(fill.Bounds.Initialized); Assert.True(fill.Bounds.ParentBounds.Initialized);
        composition.View.LayoutCompleted(new(0, 0, 1800, 1800)); fill.RenderInteractiveElements(0);
        // Assert
        Assert.Equal(target, fill.State.Target); Assert.Equal(glyphs, fixture.Glyphs.Count); Assert.Equal(textures, fixture.LoadRequests.Count);
        Assert.True(clips[1].Top < clips[0].Top); Assert.Equal(clips[0].Bottom, clips[1].Bottom, 4);
        Assert.NotEqual(colors[0], colors[1]); Assert.Equal(colors[0].Alpha, colors[1].Alpha);
    }
    /// <summary>Newly visible marker rows rasterize their required size while unchanged rows reuse the same resources.</summary>
    [Fact]
    public void BodyOptionalRowsUpdateWithoutReplacingComponentsAndRasterizeNewMarkerSizes()
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 1), true); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
        var outline = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:outline"));
        var firstSize = content.Size; var firstTarget = outline.State.Target;
        int assets = fixture.LoadRequests.Count;
        // Act
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 2), true); content.Prepare(fixture.Context); composition.Update(fixture.Context);

        // Assert
        Assert.Equal(firstSize, content.Size); Assert.Equal(firstTarget, outline.State.Target);
        Assert.Same(reading, composition.Composer.GetElement("test:reading"));
        Assert.True(fixture.LoadRequests.Count > assets);
        int markerRasterCount = fixture.LoadRequests.Count;
        // Act
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 2), false); content.Prepare(fixture.Context); composition.Update(fixture.Context);

        // Assert
        Assert.Equal(0, reading.State.Target.Height); Assert.All(fixture.Glyphs, glyph => Assert.True(glyph.Disposed));
        Assert.Equal(firstSize.Height, content.Size.Height);
        // Act
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), false); content.Prepare(fixture.Context); composition.Update(fixture.Context);

        // Assert
        Assert.True(content.Size.Height < firstSize.Height); Assert.Equal(markerRasterCount, fixture.LoadRequests.Count);
    }
    /// <summary>First host composition and newly visible trend markers keep chevrons outside the temperature reading.</summary>
    [Theory]
    [InlineData(-2, 1, false)] [InlineData(2, 1, false)] [InlineData(-2, 2, false)] [InlineData(2, 2, false)]
    [InlineData(-2, 1, true)] [InlineData(2, 1, true)] [InlineData(-2, 2, true)] [InlineData(2, 2, true)]
    public void BodyTrendFirstAppearanceUsesResolvedNativeRows(int direction, float scale, bool initiallyTrending)
    {
        using var host = new HudNativeHostFixture();
        using var fixture = new LayeredIndicatorsFixture(scale);
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, initiallyTrending ? direction : 0), true);
        content.Prepare(host.Components.Context); host.Size = content.Size;
        host.Native.Setup(value => value.CreateView(host.Components.Api.Object, It.IsAny<NativeViewRole>()))
            .Returns(() => content.CreateView(host.Components.Api.Object));
        host.Synchronize();
        if (!initiallyTrending)
        {
            content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, direction), true);
            content.Prepare(host.Components.Context); host.Size = content.Size; host.Revision++;
            host.Synchronize();
        }
        var composer = host.Host.Composers[host.Group.Id];
        AssertBodyTrendRows(composer, host.Registration.Id, direction);
        int glyphs = fixture.Glyphs.Count;
        foreach (int next in new[] { -direction, 0, Math.Sign(direction), direction })
        {
            content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, next), true);
            content.Prepare(host.Components.Context); host.Revision++;
            host.Synchronize();
            Assert.Same(composer, host.Host.Composers[host.Group.Id]);
            AssertBodyTrendRows(composer, host.Registration.Id, next);
            Assert.Equal(glyphs, fixture.Glyphs.Count);
        }
        Assert.Empty(host.Failures);
    }

    /// <summary>Fill clipping intersects the supplied host clip and restores scissor state after native draw failure.</summary>
    [Fact]
    public void ThermometerClipIsPreparedAndExceptionSafe()
    {
        using var fixture = new LayeredIndicatorsFixture();
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), false); content.Prepare(fixture.Context);
        using var view = content.CreateView(fixture.Context.Api);
        ((NativeView)view).SetFailureHandler(fixture.Resources.Native.Failures.Add);
        using var composition = new IndicatorComposition(fixture.Context.Api, view, fixture.Context, content.Size);
        var fill = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:fill"));
        var clip = fill.State.Target with { X = fill.State.Target.X + 2, Width = fill.State.Target.Width - 4 };
        view.LayoutCompleted(clip);
        RectangleF observed = default;
        fixture.Resources.Native.Render.Setup(renderer => renderer.PushScissor(It.IsAny<ElementBounds>(), true))
            .Callback<ElementBounds, bool>((bounds, _) => observed = Rect(bounds));
        fixture.Resources.Native.Render.Setup(renderer => renderer.RenderTexture(It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>())).Throws(new InvalidOperationException("draw failure"));
        fill.RenderInteractiveElements(0);
        Assert.True(observed.Left >= clip.Left && observed.Right <= clip.Right);
        Assert.True(observed.Top >= clip.Top && observed.Bottom <= clip.Bottom);
        fixture.Resources.Native.Render.Verify(renderer => renderer.PopScissor(), Times.Once);
        Assert.Single(fixture.Resources.Native.Failures);
    }
    #endregion
    #region Weather layer semantics
    /// <summary>Independent weather channels retain selection order and share their complete full-canvas native origin.</summary>
    [Theory]
    [InlineData(EnumPrecipitationType.Rain)] [InlineData(EnumPrecipitationType.Snow)] [InlineData(EnumPrecipitationType.Hail)]
    public void WeatherChannelsShareOriginAndOrderedDraws(EnumPrecipitationType precipitation)
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture(2);
        using var content = new WeatherConditionsPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => fixture.Resources.Backdrop.Object, fixture.CheckPack);
        var sample = new WeatherConditionsSample(precipitation, true) { PrecipitationIntensity = 3, FogIntensity = 2, ThunderIntensity = 1 };
        content.SetContent(sample);
        // Act
        content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        // Assert
        var layers = Enumerable.Range(0, 3).Select(index => Assert.IsType<TextureElement>(composition.Composer.GetElement("test:layer-" + index))).ToArray();
        Assert.All(layers, layer => Assert.Equal(layers[0].State.Target, layer.State.Target));
        for (int index = 0; index < fixture.SvgTextures.Count; index++)
            fixture.SvgTextures[index].Texture.TextureId = 6000 + index;
        composition.View.CollectBackdrops(fixture.Resources.Native.Render.Object); composition.Composer.Render(0);
        Assert.Single(fixture.Resources.Backdrop.Invocations);
        var expected = WeatherConditionsLayers.Select(sample).Select(index => fixture.Bindings["textures/hud/weather/composite/" + WeatherConditionsLayers.Names[index] + ".svg"].TextureId);
        Assert.Equal(expected, fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "RenderTexture").Select(call => (int)call.Arguments[0]));
        // Act
        content.SetContent(new(null, false)); content.Prepare(fixture.Context); composition.Update(fixture.Context);
        fixture.Resources.Native.Render.Invocations.Clear(); composition.Composer.Render(0);
        // Assert
        Assert.DoesNotContain(fixture.Resources.Native.Render.Invocations, call => call.Method.Name == "RenderTexture");
        Assert.True(content.Size.IsEmpty);
        Assert.Equal(1, fixture.PackChecks);
    }
    /// <summary>Weather composition uses full-canvas SVGs or the established precipitation/fog fallback.</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void WeatherCompositionUsesCompleteOrFallbackArtwork(bool complete)
    {
        // Arrange
        using var fixture = new LayeredIndicatorsFixture();
        fixture.CompletePack = complete;
        var loader = new SvgRasterizationRecorder();
        loader.Setup(fixture.Resources.Native.Gui, fixture.Context.Api);
        using var content = new WeatherConditionsPresentation(loadBackdrop: _ => null, hasCompletePack: fixture.CheckPack);
        content.SetContent(new(EnumPrecipitationType.Rain, true) { ThunderIntensity = 1 });
        // Act
        content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        // Assert
        Assert.Equal(1, fixture.PackChecks);
        string[] expectedArtwork = complete
            ? ["textures/hud/weather/composite/rain-light.svg", "textures/hud/weather/composite/fog-light.svg", "textures/hud/weather/composite/thunder-light.svg"]
            : ["textures/hud/weather/rain.svg", "textures/hud/weather/fog.svg"];
        Assert.Equal(expectedArtwork.OrderBy(path => path), loader.Requests.Select(request => request.Path).OrderBy(path => path));
        Assert.Equal(loader.Requests.Select(request => (request.Width, request.Height)),
            loader.Textures.Select(texture => (texture.Width, texture.Height)));
        Assert.All(loader.Requests, request =>
        {
            Assert.Equal(request.Width, request.TextureWidth);
            Assert.Equal(request.Height, request.TextureHeight);
        });
        Assert.Single(loader.Requests.Select(request => (request.Width, request.Height)).Distinct());
        for (int index = 0; index < loader.Textures.Count; index++)
            loader.Textures[index].TextureId = 5000 + index;
        fixture.Resources.Native.Render.Invocations.Clear();
        composition.Composer.Render(0);
        var draws = fixture.Resources.Native.Render.Invocations
            .Where(call => call.Method.Name == nameof(IRenderAPI.RenderTexture)).ToArray();
        Assert.Equal(loader.Textures.Count, draws.Length);
        Assert.Equal(loader.Textures.Select(texture => (double)texture.Width),
            draws.Select(draw => Convert.ToDouble(draw.Arguments[3])));
        Assert.Equal(loader.Textures.Select(texture => (double)texture.Height),
            draws.Select(draw => Convert.ToDouble(draw.Arguments[4])));
        Assert.Empty(fixture.Resources.Native.Failures);
        if (!complete)
        {
            Assert.DoesNotContain(loader.Requests, request => request.Path.Contains("/composite/"));
            var rain = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:layer-0"));
            var fog = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:layer-1"));
            Assert.Equal(rain.State.Target.Size, fog.State.Target.Size);
            Assert.Equal(rain.State.Target.Right, fog.State.Target.Left, 4); Assert.Equal(rain.State.Target.Top, fog.State.Target.Top);
            Assert.True(rain.State.Target.Height < rain.Bounds.ParentBounds.OuterHeight);
        }
        // Act
        int rasterCount = loader.Requests.Count;
        content.Prepare(fixture.Context); composition.View.Prepare(fixture.Context);
        // Assert
        Assert.Equal(1, fixture.PackChecks);
        Assert.Equal(rasterCount, loader.Requests.Count);
    }
    /// <summary>Thunder-only data needs a complete pack; inactive weather stays hidden in both styles.</summary>
    [Theory]
    [InlineData(false, 1, HudIndicatorStyle.Icon, false)] [InlineData(true, 1, HudIndicatorStyle.Icon, true)]
    [InlineData(false, 0, HudIndicatorStyle.Icon, false)] [InlineData(true, 0, HudIndicatorStyle.Icon, false)]
    [InlineData(false, 0, HudIndicatorStyle.Text, false)] [InlineData(true, 0, HudIndicatorStyle.Text, false)]
    [InlineData(false, 1, HudIndicatorStyle.Text, true)]
    public void WeatherVisibilityRespectsPackCapabilities(bool complete, int thunder, HudIndicatorStyle style, bool visible)
    {
        using var fixture = new LayeredIndicatorsFixture { CompletePack = complete };
        using var content = new WeatherConditionsPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null, fixture.CheckPack);
        content.SetStyle(style);
        content.SetContent(new(null, false) { ThunderIntensity = thunder }); content.Prepare(fixture.Context);
        Assert.Equal(visible, !content.Size.IsEmpty);
        if (visible && style == HudIndicatorStyle.Icon) Assert.EndsWith("thunder-light.svg", Assert.Single(content.Layers).Path);
        else Assert.Empty(content.Layers);
        Assert.Empty(fixture.Bindings);
        if (!visible) Assert.Empty(fixture.Values);
    }
    /// <summary>Native text styles retain localized readings and skip artwork, masks, backdrop and pack inspection.</summary>
    [Theory]
    [InlineData("wetness", "26%")] [InlineData("body", "34°C COLD +2")] [InlineData("weather", "RAIN2 FOG2 THR1")]
    public void TextStylesRetainFeatureReadingsWithoutArtwork(string kind, string expected)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, HudIndicatorStyle.Text);
        using (overlay)
        using (var composition = Mount(fixture, overlay, presentation))
        {
            Assert.Contains(expected, fixture.Values); Assert.Equal(2, fixture.Values.Count);
            Assert.Empty(fixture.Bindings); Assert.Equal(0, fixture.PackChecks); Assert.Empty(fixture.Resources.Backdrop.Invocations);
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Checks native marker direction and centering against the actual prepared reading and icon targets.</summary>
    private static void AssertBodyTrendRows(GuiComposer composer, string prefix, int direction)
    {
        var reading = Assert.IsType<TextElement>(composer.GetElement(prefix + ":reading"));
        var outline = Assert.IsType<TextureElement>(composer.GetElement(prefix + ":outline"));
        Assert.True(reading.State.Target.Bottom <= outline.State.Target.Top);
        for (int index = 0; index < Math.Abs(direction); index++)
        {
            var marker = Assert.IsType<TextureElement>(composer.GetElement(prefix + ":trend-" + index));
            Assert.True(direction > 0 ? marker.State.Target.Bottom <= reading.State.Target.Top : marker.State.Target.Top >= reading.State.Target.Bottom,
                $"trend={direction}, marker={marker.State.Target}, reading={reading.State.Target}");
            Assert.True(marker.State.Target.Bottom <= outline.State.Target.Top);
            Assert.Equal(reading.State.Target.X + reading.State.Target.Width / 2,
                marker.State.Target.X + marker.State.Target.Width / 2, 4);
        }
    }
    /// <summary>Mounts the actual controller's native capability and wires its component failure boundary.</summary>
    private static IndicatorComposition Mount(LayeredIndicatorsFixture fixture, IHudOverlay overlay, IndicatorPresentation presentation, NativeViewRole role = NativeViewRole.Live) =>
        new(fixture.Context.Api, ViewFactory.Create(overlay, fixture.Context.Api, role, fixture.Resources.Native.Failures.Add), fixture.Context, presentation.Size);
    /// <summary>Gets the registered components for the selected production presentation.</summary>
    private static GuiElement[] Components(IndicatorComposition composition, string kind, HudIndicatorStyle style)
    {
        string[] keys = style == HudIndicatorStyle.Text ? ["name", "reading"] : kind switch
        {
            "wetness" => ["drop-0", "drop-1", "drop-2", "drop-3", "reading"],
            "body" => ["fill", "outline", "trend-0", "trend-1", "reading"],
            _ => ["layer-0", "layer-1", "layer-2"]
        };
        return keys.Select(key => composition.Composer.GetElement("test:" + key)).ToArray();
    }
    /// <summary>Reads cached component state without touching resource creation or layout.</summary>
    private static ElementState State(GuiElement element) => element is TextureElement texture ? texture.State : ((TextElement)element).State;
    /// <summary>Captures framebuffer bounds at the moment of a mocked scissor submission.</summary>
    private static RectangleF Rect(ElementBounds bounds) => new((float)bounds.renderX, (float)bounds.renderY, (float)bounds.OuterWidth, (float)bounds.OuterHeight);
    /// <summary>Checks host-created thermometer rasters against resolved bounds, loader identity and actual native draw calls.</summary>
    private static (string Path, LoadedTexture Texture)[] AssertHostThermometerRasters(
        HudNativeHostFixture host, SvgRasterizationRecorder loader, int firstTextureId)
    {
        string prefix = host.Registration.Id + ":";
        string[] paths = ["textures/hud/body-temperature/fill-mask.svg", "textures/hud/body-temperature/outline.svg"];
        var current = new List<(string Path, TextureElement Element, LoadedTexture Texture, RectangleF Target)>();
        foreach (string path in paths)
        {
            string key = path.EndsWith("/fill-mask.svg", StringComparison.Ordinal) ? "fill" : "outline";
            var element = Assert.IsType<TextureElement>(host.Host.Composers[host.Group.Id].GetElement(prefix + key));
            RectangleF expected = ExpectedTruncatedPixelTarget(element.Bounds);
            int requestIndex = loader.Requests.FindLastIndex(request => request.Path == path);
            Assert.True(requestIndex >= 0);
            var request = loader.Requests[requestIndex];
            var texture = loader.Textures[requestIndex];
            Assert.Equal(new Size((int)expected.Width, (int)expected.Height), new Size(request.Width, request.Height));
            Assert.Equal(new Size(request.Width, request.Height), new Size(request.TextureWidth, request.TextureHeight));
            Assert.Equal(new Size(request.Width, request.Height), new Size(texture.Width, texture.Height));
            Assert.Equal(expected, element.State.Target);
            Assert.False(texture.Disposed);
            texture.TextureId = firstTextureId++;
            current.Add((path, element, texture, expected));
        }

        host.Components.Render.Invocations.Clear();
        host.Render();
        var draws = host.Components.Render.Invocations
            .Where(call => call.Method.Name == nameof(IRenderAPI.RenderTexture)).ToArray();
        Assert.Equal(current.Count, draws.Length);
        foreach (var item in current)
        {
            var draw = Assert.Single(draws.Where(call => (int)call.Arguments[0] == item.Texture.TextureId));
            Assert.Equal(item.Texture.Width, Convert.ToDouble(draw.Arguments[3]));
            Assert.Equal(item.Texture.Height, Convert.ToDouble(draw.Arguments[4]));
            Assert.Equal(item.Target.Width, Convert.ToDouble(draw.Arguments[3]));
            Assert.Equal(item.Target.Height, Convert.ToDouble(draw.Arguments[4]));
        }
        return current.Select(item => (item.Path, item.Texture)).ToArray();
    }

    /// <summary>Independently truncates native element rectangle edges toward zero.</summary>
    private static RectangleF ExpectedTruncatedPixelTarget(ElementBounds bounds)
    {
        double left = Math.Truncate(bounds.renderX);
        double top = Math.Truncate(bounds.renderY);
        double right = Math.Truncate(bounds.renderX + bounds.OuterWidth);
        double bottom = Math.Truncate(bounds.renderY + bounds.OuterHeight);
        return new((float)left, (float)top, (float)(right - left), (float)(bottom - top));
    }

    /// <summary>Matches the native ref-wrapper cache-loading boundary.</summary>
    private delegate void TextureLoad(AssetLocation location, ref LoadedTexture texture);
    #endregion
}
