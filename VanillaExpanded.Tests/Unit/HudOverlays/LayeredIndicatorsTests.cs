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
            Assert.Throws<InvalidOperationException>(() => overlay.Draw(fixture.Resources.Native.Render.Object, composition.Composer.Bounds, new(0, 0, 900, 900), 0));
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
            int measurements = fixture.Measurements, glyphs = fixture.Glyphs.Count, textures = fixture.Bindings.Count, checks = fixture.PackChecks;
            element.Bounds.fixedWidth += 77; element.Bounds.MarkDirtyRecursive();
            for (int frame = 0; frame < 4; frame++)
            {
                ((INativePresentation)overlay).BeginFrame(.1f); composition.View.SetOpacity(.6f);
                composition.View.CollectBackdrops(fixture.Resources.Native.Render.Object); composition.Composer.Render(.1f);
            }
            Assert.Equal(target, state.Target); Assert.False(element.Bounds.Initialized);
            Assert.Equal(measurements, fixture.Measurements); Assert.Equal(glyphs, fixture.Glyphs.Count);
            Assert.Equal(textures, fixture.Bindings.Count); Assert.Equal(checks, fixture.PackChecks);
        }
    }
    /// <summary>Content and style updates preserve controller state and release only the retired composer's owned resources.</summary>
    [Theory]
    [InlineData("wetness", HudIndicatorStyle.Icon)] [InlineData("wetness", HudIndicatorStyle.Text)]
    [InlineData("body", HudIndicatorStyle.Icon)] [InlineData("body", HudIndicatorStyle.Text)]
    [InlineData("weather", HudIndicatorStyle.Icon)] [InlineData("weather", HudIndicatorStyle.Text)]
    public void IndependentViewsOwnGlyphsAndBorrowArtworkAcrossUpdatesAndStyles(string kind, HudIndicatorStyle style)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        {
            using var live = Mount(fixture, overlay, presentation);
            int liveCount = fixture.Glyphs.Count;
            using var source = Mount(fixture, overlay, presentation, NativeViewRole.SourceTrace);
            var sourceGlyphs = fixture.Glyphs.Skip(liveCount).ToArray();
            Assert.NotSame(Components(live, kind, style)[0], Components(source, kind, style)[0]);
            change(); prepare(); live.Update(fixture.Context);
            int beforeSource = fixture.Glyphs.Count; source.Update(fixture.Context);
            var survivingGlyphs = sourceGlyphs.Concat(fixture.Glyphs.Skip(beforeSource)).Where(glyph => !glyph.Disposed).ToArray();
            live.Dispose();
            source.View.SetOpacity(.5f); source.Composer.Render(0);
            if (kind != "weather" || style == HudIndicatorStyle.Text) Assert.NotEmpty(survivingGlyphs);
            Assert.All(survivingGlyphs, glyph => Assert.False(glyph.Disposed));
            var alternate = style == HudIndicatorStyle.Icon ? HudIndicatorStyle.Text : HudIndicatorStyle.Icon;
            presentation.SetStyle(alternate); prepare();
            using var replacement = Mount(fixture, overlay, presentation);
            source.Dispose(); replacement.Composer.Render(0);
            replacement.Dispose();
            Assert.All(fixture.Glyphs, glyph => Assert.True(glyph.Disposed));
            Assert.All(fixture.Bindings.Values, texture => Assert.False(texture.Disposed));
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    /// <summary>Movement reuses assets and glyphs; scale/context changes replace only glyph resources and native geometry.</summary>
    [Theory]
    [InlineData("wetness")] [InlineData("body")] [InlineData("weather")]
    public void MovementAndScaleInvalidateOnlyRequiredWork(string kind)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create(kind, HudIndicatorStyle.Icon);
        using (overlay)
        using (var composition = Mount(fixture, overlay, presentation))
        {
            var element = Components(composition, kind, HudIndicatorStyle.Icon)[0];
            var initial = State(element).Target;
            int textures = fixture.Bindings.Count, glyphs = fixture.Glyphs.Count;
            composition.Composer.Bounds.fixedX += 17; composition.Composer.Bounds.fixedY += 23;
            composition.Composer.Bounds.MarkDirtyRecursive(); composition.Composer.Bounds.CalcWorldBounds();
            composition.View.LayoutCompleted(new(0, 0, 1800, 1800));
            Assert.Equal(initial.X + 17, State(element).Target.X, 4); Assert.Equal(initial.Y + 23, State(element).Target.Y, 4);
            Assert.Equal(glyphs, fixture.Glyphs.Count); Assert.Equal(textures, fixture.Bindings.Count);
            var beforeScale = State(element).Target;
            Vintagestory.API.Config.RuntimeEnv.GUIScale = 2; prepare(); composition.Update(fixture.Context);
            Assert.Equal(beforeScale.Width * 2, State(element).Target.Width, 3);
            Assert.Equal(textures, fixture.Bindings.Count);
            if (glyphs > 0) Assert.All(fixture.Glyphs.Take(glyphs), glyph => Assert.True(glyph.Disposed));
            int beforeContext = fixture.Glyphs.Count;
            composition.Update(new(fixture.Context.Api, 2, "fr", 1));
            if (glyphs > 0) Assert.True(fixture.Glyphs.Count > beforeContext);
        }
    }
    /// <summary>Production PNG loaders borrow cache wrappers, while each generated glyph is deleted exactly once.</summary>
    [Theory]
    [InlineData("wetness")] [InlineData("body")] [InlineData("weather")]
    public void ProductionLoadersKeepEngineTextureOwnership(string kind)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var loaded = new List<LoadedTexture>();
        var paths = new List<string>();
        fixture.Resources.Native.Render.Setup(renderer => renderer.GetOrLoadTexture(It.IsAny<AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new TextureLoad((AssetLocation location, ref LoadedTexture texture) => { paths.Add(location.Path); loaded.Add(texture); }));
        IndicatorPresentation presentation;
        INativeView view;
        switch (kind)
        {
            case "wetness":
                var wetness = new PlayerWetnessPresentation(fixture.Measure, fixture.CreateText, loadBackdrop: _ => null);
                wetness.SetContent(.5f); wetness.Prepare(fixture.Context); presentation = wetness; view = wetness.CreateView(fixture.Context.Api); break;
            case "body":
                var body = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, loadBackdrop: _ => null);
                body.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 2), true); body.Prepare(fixture.Context); presentation = body; view = body.CreateView(fixture.Context.Api); break;
            default:
                var weather = new WeatherConditionsPresentation(loadBackdrop: _ => null, hasCompletePack: _ => true);
                weather.SetContent(new(null, false) { CloudCoverage = 0 }); weather.Prepare(fixture.Context); presentation = weather; view = weather.CreateView(fixture.Context.Api); break;
        }
        using (presentation)
        using (var composition = new IndicatorComposition(fixture.Context.Api, view, fixture.Context, presentation.Size))
        {
            Assert.NotEmpty(loaded); Assert.All(paths, path => Assert.EndsWith(".png", path));
            Assert.All(loaded, texture => Assert.True(texture.IgnoreUndisposed));
            var glyphIds = fixture.Glyphs.Select(glyph => glyph.TextureId).ToArray();
            composition.Dispose(); composition.Dispose();
            Assert.All(loaded, texture => Assert.False(texture.Disposed));
            foreach (int id in glyphIds) fixture.Resources.Native.Gui.Verify(gui => gui.DeleteTexture(id), Times.Once);
        }
    }
    #endregion
    #region Wetness and thermometer geometry
    /// <summary>Wetness percentage changes leave both fully filled silhouettes and their aligned outlines unchanged.</summary>
    [Theory]
    [InlineData(.001f, "1%")] [InlineData(.5f, "50%")] [InlineData(1, "100%")]
    public void WetnessReadingsFitAboveStableFullDrops(float wetness, string expected)
    {
        using var fixture = new LayeredIndicatorsFixture(2) { TextSize = new(140, 32) };
        using var content = new PlayerWetnessPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => fixture.Resources.Backdrop.Object);
        content.SetContent(wetness); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        Assert.Contains(expected, fixture.Values);
        var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
        var drops = Enumerable.Range(0, 4).Select(index => Assert.IsType<TextureElement>(composition.Composer.GetElement("test:drop-" + index))).ToArray();
        Assert.Equal(drops[0].State.Target, drops[1].State.Target); Assert.Equal(drops[2].State.Target, drops[3].State.Target);
        Assert.True(reading.State.Target.Bottom <= drops.Min(drop => drop.State.Target.Top) + .001);
        Assert.Equal(reading.Bounds.renderX + reading.Bounds.OuterWidth / 2,
            drops[0].Bounds.ParentBounds.renderX + drops[0].Bounds.ParentBounds.OuterWidth / 2, 4);
        var targets = drops.Select(drop => drop.State.Target).ToArray();
        content.SetContent(.26f); content.Prepare(fixture.Context); composition.Update(fixture.Context);
        Assert.Equal(targets, drops.Select(drop => drop.State.Target));
        fixture.Resources.Native.Render.Invocations.Clear(); composition.Composer.Render(0);
        var draws = fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "Render2DTexture").ToArray();
        Assert.Equal(4, draws.Length);
        Assert.Equal(draws[0].Arguments[0], draws[2].Arguments[0]); Assert.Equal(draws[1].Arguments[0], draws[3].Arguments[0]);
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
        Assert.Equal(showReading ? 1 : 0, fixture.Glyphs.Count);
        Assert.Equal(Math.Abs(direction) == 0 ? 2 : 3, fixture.Bindings.Count);
        if (direction != 0) Assert.Contains(fixture.Bindings.Keys, path => path.EndsWith(direction > 0 ? "chevron-up.png" : "chevron-down.png"));
    }
    /// <summary>Fill-only updates refresh the prepared reveal without dirtying native bounds or regenerating resources.</summary>
    [Fact]
    public void ThermometerFillChangesClipWithoutLayoutOrGlyphChanges()
    {
        using var fixture = new LayeredIndicatorsFixture();
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), true); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        var fill = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:fill"));
        var target = fill.State.Target; int glyphs = fixture.Glyphs.Count, textures = fixture.Bindings.Count;
        var clips = new List<RectangleF>();
        var colors = new List<(float Red, float Green, float Blue, float Alpha)>();
        fixture.Resources.Native.Render.Setup(renderer => renderer.PushScissor(It.IsAny<ElementBounds>(), true))
            .Callback<ElementBounds, bool>((bounds, _) => clips.Add(Rect(bounds)));
        fixture.Resources.Native.Render.Setup(renderer => renderer.RenderTexture(It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback<int, double, double, double, double, float, Vec4f>((_, _, _, _, _, _, tint) => colors.Add((tint.X, tint.Y, tint.Z, tint.W)));
        fill.RenderInteractiveElements(0);
        content.SetContent(new(34.01f, 37, BodyTemperatureRisk.Freezing, .75f), true); content.Prepare(fixture.Context);
        composition.View.Prepare(fixture.Context);
        Assert.True(fill.Bounds.Initialized); Assert.True(fill.Bounds.ParentBounds.Initialized);
        composition.View.LayoutCompleted(new(0, 0, 1800, 1800)); fill.RenderInteractiveElements(0);
        Assert.Equal(target, fill.State.Target); Assert.Equal(glyphs, fixture.Glyphs.Count); Assert.Equal(textures, fixture.Bindings.Count);
        Assert.True(clips[1].Top < clips[0].Top); Assert.Equal(clips[0].Bottom, clips[1].Bottom, 4);
        Assert.NotEqual(colors[0], colors[1]); Assert.Equal(colors[0].Alpha, colors[1].Alpha);
    }
    /// <summary>Optional reading and marker strength changes reuse component identities and preserve the stable trend column.</summary>
    [Fact]
    public void BodyOptionalRowsUpdateWithoutReplacingComponentsOrArtwork()
    {
        using var fixture = new LayeredIndicatorsFixture();
        using var content = new BodyTemperatureThermometerPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 1), true); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
        var outline = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:outline"));
        var firstSize = content.Size; var firstTarget = outline.State.Target;
        int assets = fixture.Bindings.Count;
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 2), true); content.Prepare(fixture.Context); composition.Update(fixture.Context);
        Assert.Equal(firstSize, content.Size); Assert.Equal(firstTarget, outline.State.Target);
        Assert.Same(reading, composition.Composer.GetElement("test:reading")); Assert.Equal(assets, fixture.Bindings.Count);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f, 2), false); content.Prepare(fixture.Context); composition.Update(fixture.Context);
        Assert.Equal(0, reading.State.Target.Height); Assert.All(fixture.Glyphs, glyph => Assert.True(glyph.Disposed));
        Assert.Equal(firstSize.Height, content.Size.Height);
        content.SetContent(new(34, 37, BodyTemperatureRisk.Cold, .25f), false); content.Prepare(fixture.Context); composition.Update(fixture.Context);
        Assert.True(content.Size.Height < firstSize.Height); Assert.Equal(assets, fixture.Bindings.Count);
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
        using var fixture = new LayeredIndicatorsFixture(2);
        using var content = new WeatherConditionsPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => fixture.Resources.Backdrop.Object, fixture.CheckPack);
        var sample = new WeatherConditionsSample(precipitation, true) { CloudCoverage = 3, PrecipitationIntensity = 3, FogIntensity = 2, ThunderIntensity = 1 };
        content.SetContent(sample); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        var layers = Enumerable.Range(0, 4).Select(index => Assert.IsType<TextureElement>(composition.Composer.GetElement("test:layer-" + index))).ToArray();
        Assert.All(layers, layer => Assert.Equal(layers[0].State.Target, layer.State.Target));
        composition.View.CollectBackdrops(fixture.Resources.Native.Render.Object); composition.Composer.Render(0);
        Assert.Single(fixture.Resources.Backdrop.Invocations);
        var expected = WeatherConditionsLayers.Select(sample).Select(index => fixture.Bindings["textures/hud/weather/composite/" + WeatherConditionsLayers.Names[index] + ".png"].TextureId);
        Assert.Equal(expected, fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "RenderTexture").Select(call => (int)call.Arguments[0]));
        content.SetContent(new(null, false) { CloudCoverage = 0 }); content.Prepare(fixture.Context); composition.Update(fixture.Context);
        fixture.Resources.Native.Render.Invocations.Clear(); composition.Composer.Render(0);
        Assert.Single(fixture.Resources.Native.Render.Invocations, call => call.Method.Name == "RenderTexture");
        Assert.Equal(1, fixture.PackChecks);
    }
    /// <summary>A partial composite pack selects precipitation/fog together and never requests any composite sprite.</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void WeatherPackDetectionLoadsOnlySelectedCompleteOrFallbackArtwork(bool complete)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var assets = new Mock<IAssetManager>(); fixture.Resources.Native.Api.SetupGet(api => api.Assets).Returns(assets.Object);
        assets.Setup(manager => manager.Exists(It.IsAny<AssetLocation>()))
            .Returns((AssetLocation location) => complete || !location.Path.EndsWith("thunder-heavy.png"));
        using var content = new WeatherConditionsPresentation(load: fixture.Load, loadBackdrop: _ => null);
        content.SetContent(new(EnumPrecipitationType.Rain, true) { CloudCoverage = 4, ThunderIntensity = 1 }); content.Prepare(fixture.Context);
        using var composition = new IndicatorComposition(fixture.Context.Api, content.CreateView(fixture.Context.Api), fixture.Context, content.Size);
        Assert.Equal(complete ? 4 : 2, fixture.Bindings.Count);
        Assert.All(fixture.Bindings.Keys, path => Assert.Equal(complete, path.Contains("/composite/")));
        if (!complete)
        {
            var rain = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:layer-0"));
            var fog = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:layer-1"));
            Assert.Equal(rain.State.Target.Size, fog.State.Target.Size);
            Assert.Equal(rain.State.Target.Right, fog.State.Target.Left, 4); Assert.Equal(rain.State.Target.Top, fog.State.Target.Top);
            Assert.True(rain.State.Target.Height < rain.Bounds.ParentBounds.OuterHeight);
        }
        int checks = assets.Invocations.Count; content.Prepare(fixture.Context); composition.View.Prepare(fixture.Context);
        Assert.Equal(checks, assets.Invocations.Count);
    }
    /// <summary>The incomplete pack hides unsupported sky/thunder-only data while the complete pack shows clear or unknown sky.</summary>
    [Theory]
    [InlineData(false, 0, false)] [InlineData(true, 0, true)] [InlineData(true, null, true)]
    public void WeatherVisibilityRespectsPackCapabilities(bool complete, int? cloud, bool visible)
    {
        using var fixture = new LayeredIndicatorsFixture { CompletePack = complete };
        using var content = new WeatherConditionsPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => null, fixture.CheckPack);
        content.SetContent(new(null, false) { CloudCoverage = cloud, ThunderIntensity = 1 }); content.Prepare(fixture.Context);
        Assert.Equal(visible, !content.Size.IsEmpty);
        if (visible) Assert.EndsWith(cloud.HasValue ? "sky-clear.png" : "sky-unknown.png", content.Layers[0].Path);
        else Assert.Empty(content.Layers);
    }
    /// <summary>Native text styles retain localized readings and skip artwork, masks, backdrop and pack inspection.</summary>
    [Theory]
    [InlineData("wetness", "26%")] [InlineData("body", "34°C COLD +2")] [InlineData("weather", "CLD4 RAIN2 FOG2 THR1")]
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
            _ => ["layer-0", "layer-1", "layer-2", "layer-3"]
        };
        return keys.Select(key => composition.Composer.GetElement("test:" + key)).ToArray();
    }
    /// <summary>Reads cached component state without touching resource creation or layout.</summary>
    private static ElementState State(GuiElement element) => element is TextureElement texture ? texture.State : ((TextElement)element).State;
    /// <summary>Captures framebuffer bounds at the moment of a mocked scissor submission.</summary>
    private static RectangleF Rect(ElementBounds bounds) => new((float)bounds.renderX, (float)bounds.renderY, (float)bounds.OuterWidth, (float)bounds.OuterHeight);
    /// <summary>Matches the native ref-wrapper cache-loading boundary.</summary>
    private delegate void TextureLoad(AssetLocation location, ref LoadedTexture texture);
    #endregion
}
