using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks actual native registration, prepared instrument geometry, calibration and composition-owned resources.</summary>
[Collection("HudOverlayGeometry")]
public sealed class InstrumentIndicatorsTests
{
    #region Public API
    #region Native registration and rendering
    /// <summary>Every instrument/style uses passive native components and canonical preparation allocates no GUI resources.</summary>
    [Theory]
    [InlineData("time", EnvironmentalGaugeStyle.Immersive)] [InlineData("time", EnvironmentalGaugeStyle.Icon)] [InlineData("time", EnvironmentalGaugeStyle.Text)]
    [InlineData("temperature", EnvironmentalGaugeStyle.Immersive)] [InlineData("temperature", EnvironmentalGaugeStyle.Icon)] [InlineData("temperature", EnvironmentalGaugeStyle.Text)]
    [InlineData("date", EnvironmentalGaugeStyle.Immersive)] [InlineData("date", EnvironmentalGaugeStyle.Icon)] [InlineData("date", EnvironmentalGaugeStyle.Text)]
    public void AllStylesRegisterNativePassiveComponents(string kind, EnvironmentalGaugeStyle style)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, style);
        using (overlay)
        {
            Assert.Empty(fixture.Resources.Owned); Assert.Empty(fixture.Resources.Artwork); Assert.Empty(fixture.Faces); Assert.Empty(fixture.Ticks); Assert.Empty(fixture.Uploads);
            using var composition = fixture.Mount(overlay, presentation);
            foreach (var element in Components(composition, kind, style))
            {
                Assert.False(element.Focusable);
                Assert.NotNull(element.Bounds.ParentBounds);
                var mouse = new MouseEvent(0, 0, EnumMouseButton.Left, 0);
                element.OnMouseDownOnElement(fixture.Context.Api, mouse); Assert.False(mouse.Handled);
            }
            composition.Composer.Render(.1f);
            Assert.Throws<InvalidOperationException>(() => overlay.Draw(fixture.Resources.Native.Render.Object, composition.Composer.Bounds, new(0, 0, 100, 100), 0));
            if (style == EnvironmentalGaugeStyle.Text)
            {
                Assert.Empty(fixture.Resources.Artwork); Assert.Empty(fixture.Faces); Assert.Empty(fixture.Ticks); Assert.Empty(fixture.Uploads);
                Assert.Contains(kind switch { "time" => "04:30", "temperature" => "12°C", _ => "OCT 14" }, fixture.Resources.Values);
            }
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    /// <summary>Repeated drawing never creates resources, measures text, recalculates bounds or touches gameplay services.</summary>
    [Theory]
    [InlineData("time", EnvironmentalGaugeStyle.Immersive)] [InlineData("time", EnvironmentalGaugeStyle.Icon)] [InlineData("time", EnvironmentalGaugeStyle.Text)]
    [InlineData("temperature", EnvironmentalGaugeStyle.Immersive)] [InlineData("temperature", EnvironmentalGaugeStyle.Icon)] [InlineData("temperature", EnvironmentalGaugeStyle.Text)]
    [InlineData("date", EnvironmentalGaugeStyle.Immersive)] [InlineData("date", EnvironmentalGaugeStyle.Icon)] [InlineData("date", EnvironmentalGaugeStyle.Text)]
    public void RenderingConsumesPreparedStateOnly(string kind, EnvironmentalGaugeStyle style)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, style);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var element = Components(composition, kind, style)[0];
            var target = State(element).Target;
            var resources = Counts(fixture);
            element.Bounds.fixedWidth += 33; element.Bounds.MarkDirtyRecursive();
            fixture.Resources.Native.Api.Invocations.Clear();
            for (int frame = 0; frame < 4; frame++)
            {
                ((INativePresentation)overlay).BeginFrame(.1f); composition.View.SetOpacity(.6f);
                composition.View.CollectBackdrops(fixture.Resources.Native.Render.Object); composition.Composer.Render(.1f);
            }
            Assert.Equal(resources, Counts(fixture)); Assert.Equal(target, State(element).Target); Assert.False(element.Bounds.Initialized);
            Assert.DoesNotContain(fixture.Resources.Native.Api.Invocations, call => call.Method.Name is "get_World" or "get_Event");
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    #endregion

    #region Geometry and calibration
    /// <summary>Localized compact readings occupy native rows above centered icons; time omits the reading and its gap.</summary>
    [Theory]
    [InlineData("time", 1)] [InlineData("temperature", 1)] [InlineData("date", 1)]
    [InlineData("time", 2)] [InlineData("temperature", 2)] [InlineData("date", 2)]
    public void CompactRowsRetainAbsentReadingAndTextAboveOrder(string kind, float scale)
    {
        using var fixture = new InstrumentIndicatorsFixture(scale);
        var (overlay, presentation, _, _) = fixture.Create(kind, EnvironmentalGaugeStyle.Icon);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
            var icon = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:icon"));
            if (kind == "time")
            {
                Assert.Equal(0, reading.Bounds.OuterHeight); Assert.Empty(fixture.Resources.Owned);
                Assert.Equal(presentation.Size.Height * scale, icon.Bounds.OuterHeight, 4);
            }
            else
            {
                Assert.True(reading.State.Target.Bottom <= icon.State.Target.Top);
                Assert.Equal(reading.State.Target.X + reading.State.Target.Width / 2, icon.State.Target.X + icon.State.Target.Width / 2, 4);
                Assert.True(reading.State.Target.Width <= reading.Bounds.OuterWidth + .001);
            }
            Assert.DoesNotContain(fixture.Resources.Paths, path => path.Contains("workshop-gauges"));
        }
    }
    /// <summary>Authored mechanism regions share native parents and full canvases, preserving aspect ratios and layer order.</summary>
    [Theory]
    [InlineData("time", 1)] [InlineData("temperature", 1)] [InlineData("date", 1)]
    [InlineData("time", 2)] [InlineData("temperature", 2)] [InlineData("date", 2)]
    public void ImmersiveLayersRespectRegisteredGeometryAndOrder(string kind, float scale)
    {
        using var fixture = new InstrumentIndicatorsFixture(scale);
        var (overlay, presentation, _, _) = fixture.Create(kind, EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var elements = Components(composition, kind, EnvironmentalGaugeStyle.Immersive);
            var first = State(elements[0]).Target; var last = State(elements[^1]).Target;
            Assert.Equal(first, last);
            Assert.Equal(presentation.Size.Width / presentation.Size.Height, first.Width / first.Height, 4);
            Assert.All(elements, element => Assert.True(Contains(first, State(element).Target)));
            if (kind == "temperature")
            {
                var empty = composition.Composer.GetElement("test:empty");
                Assert.Same(empty.Bounds.ParentBounds, composition.Composer.GetElement("test:reading").Bounds.ParentBounds);
                Assert.Equal(State(empty).Target, State(composition.Composer.GetElement("test:liquid")).Target);
                Assert.Equal(State(empty).Target, State(composition.Composer.GetElement("test:ticks")).Target);
                Assert.Equal(State(empty).Target, State(composition.Composer.GetElement("test:clamps")).Target);
            }
            if (kind == "date")
                Assert.True(State(elements[1]).Target.Right < State(elements[2]).Target.Left);
            fixture.Resources.Native.Render.Invocations.Clear(); composition.Composer.Render(0);
            var draws = Draws(fixture);
            if (kind == "date") Assert.Equal(4, draws.Length);
            if (kind == "time") Assert.Equal(4, draws.Length); // Face, shadow, hand, glass.
            var depths = draws.Select(call => Convert.ToSingle(call.Arguments[5])).ToArray();
            Assert.Equal(depths.Order().ToArray(), depths);
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    /// <summary>Clamped liquid updates retain ticks, glyphs, native bounds and mask identity while actual text stays unclamped.</summary>
    [Theory]
    [InlineData(-55, 0, "-55°C")] [InlineData(0, .4f, "0°C")] [InlineData(75, 1, "75°C")]
    public void ClimateFillAndNumericReadingRemainIndependent(float degrees, float fill, string expected)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create("temperature", EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var climate = Assert.IsType<EnvironmentalTemperaturePresentation>(presentation);
            var bounds = composition.Composer.GetElement("test:liquid").Bounds;
            var glyph = Assert.Single(fixture.Resources.Owned); var tick = Assert.Single(fixture.Ticks);
            var mask = Assert.Single(fixture.Uploads);
            change(); prepare(); composition.View.Prepare(fixture.Context);
            Assert.True(bounds.Initialized); Assert.Single(fixture.Resources.Owned); Assert.False(glyph.Disposed); Assert.False(tick.Disposed);
            Assert.Same(mask, fixture.Uploads.Last()); Assert.Equal(1, fixture.Decodes);
            climate.SetContent(degrees); prepare(); composition.View.Prepare(fixture.Context);
            Assert.Equal(fill, climate.MarkerPosition, 5); Assert.Equal(expected, climate.Reading);
            Assert.Contains(expected, fixture.Resources.Values);
            Assert.Single(fixture.Ticks); Assert.True(bounds.Initialized);
        }
    }
    /// <summary>Pointer-only updates reuse solar pixels; changed distribution replaces them or refreshes them safely in place.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SolarFaceCachingAndPointerRotation(bool inPlace)
    {
        using var fixture = new InstrumentIndicatorsFixture { ReuseFace = inPlace };
        var (overlay, presentation, prepare, change) = fixture.Create("time", EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var time = Assert.IsType<ApproximateTimePresentation>(presentation);
            var old = Assert.Single(fixture.Faces);
            var pointer = composition.Composer.GetElement("test:pointer");
            change(); prepare(); composition.View.Prepare(fixture.Context);
            Assert.Single(fixture.Faces); Assert.True(pointer.Bounds.Initialized);
            fixture.Resources.Native.Render.Invocations.Clear(); composition.Composer.Render(0);
            var angles = fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "GlRotate").ToArray();
            Assert.Equal(2, angles.Length);
            Assert.All(angles, call => Assert.Equal(time.Rotation * 180 / MathF.PI, Convert.ToSingle(call.Arguments[0]), 4));
            time.SetContent(ApproximateTimePeriod.Night, .9, ApproximateTimeDayPhases.Sample(0, _ => -.5f));
            prepare(); composition.View.Prepare(fixture.Context);
            Assert.Equal(2, fixture.Faces.Count); Assert.Equal(!inPlace, old.Disposed);
            composition.Dispose(); Assert.All(fixture.Faces, face => Assert.True(face.Disposed));
        }
    }
    #endregion

    #region Lifetime and invalidation
    /// <summary>Same-size updates retain native component bounds and replace only changed glyph or artwork content.</summary>
    [Theory]
    [InlineData("time", EnvironmentalGaugeStyle.Immersive)] [InlineData("time", EnvironmentalGaugeStyle.Icon)] [InlineData("time", EnvironmentalGaugeStyle.Text)]
    [InlineData("temperature", EnvironmentalGaugeStyle.Immersive)] [InlineData("temperature", EnvironmentalGaugeStyle.Icon)] [InlineData("temperature", EnvironmentalGaugeStyle.Text)]
    [InlineData("date", EnvironmentalGaugeStyle.Immersive)] [InlineData("date", EnvironmentalGaugeStyle.Icon)] [InlineData("date", EnvironmentalGaugeStyle.Text)]
    public void SameSizeUpdatesRetainBoundsAndUnchangedResources(string kind, EnvironmentalGaugeStyle style)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var elements = Components(composition, kind, style); var size = presentation.Size;
            var targets = elements.Select(element => State(element).Target).ToArray();
            var glyphs = fixture.Resources.Owned.ToArray();
            int art = fixture.Resources.Artwork.Count;
            change(); prepare(); composition.View.Prepare(fixture.Context);
            Assert.Equal(size, presentation.Size);
            Assert.All(elements, element => Assert.True(element.Bounds.Initialized));
            Assert.Equal(targets, elements.Select(element => State(element).Target).ToArray());
            if (kind != "date") Assert.All(glyphs, glyph => Assert.False(glyph.Disposed));
            if (kind != "time" || style != EnvironmentalGaugeStyle.Icon) Assert.Equal(art, fixture.Resources.Artwork.Count);
            int currentGlyphs = fixture.Resources.Owned.Count;
            composition.View.Prepare(fixture.Context);
            Assert.Equal(currentGlyphs, fixture.Resources.Owned.Count);
        }
    }
    /// <summary>Wide supplied localized glyph metrics determine compact measurement and remain contained at native scale.</summary>
    [Theory]
    [InlineData(1, 113, 17)] [InlineData(2, 217, 23)]
    public void CompactLocalizedMeasurementMatchesNativeGlyphs(float scale, float width, float height)
    {
        using var fixture = new InstrumentIndicatorsFixture(scale);
        var api = fixture.Context.Api;
        using var presentation = new VanillaExpanded.CalendarDate.CalendarDatePresentation(
            (_, _) => new(width, height),
            (context, _, _) => new(context.Api, 91, (int)(width * context.GuiScale), (int)(height * context.GuiScale)), fixture.Resources.Load);
        presentation.SetContent(new(14, EnumMonth.October)); presentation.SetStyle(EnvironmentalGaugeStyle.Icon); presentation.Prepare(fixture.Context);
        using var overlay = new VanillaExpanded.CalendarDate.CalendarDateOverlay(presentation);
        using var composition = fixture.Mount(overlay, presentation);
        var text = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
        Assert.Equal(width, presentation.Size.Width);
        Assert.Equal(width * scale, text.State.Target.Width, 4);
        Assert.Equal(height * scale, text.State.Target.Height, 4);
        Assert.True(Contains(new((float)text.Bounds.renderX, (float)text.Bounds.renderY, (float)text.Bounds.OuterWidth,
            (float)text.Bounds.OuterHeight), text.State.Target));
    }
    /// <summary>All custom instrument components forward supplied host clips through native intersecting scissors.</summary>
    [Theory]
    [InlineData("time")] [InlineData("temperature")] [InlineData("date")]
    public void NativeInstrumentClippingUsesPreparedHostRectangle(string kind)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var clip = new RectangleF(23, 37, 41, 53);
            composition.View.LayoutCompleted(clip);
            fixture.Resources.Native.Render.Invocations.Clear(); composition.Composer.Render(0);
            var calls = fixture.Resources.Native.Render.Invocations.Where(call => call.Method.Name == "PushScissor").ToArray();
            Assert.NotEmpty(calls);
            foreach (var call in calls)
            {
                var bounds = Assert.IsAssignableFrom<ElementBounds>(call.Arguments[0]);
                Assert.Equal(clip.X, bounds.renderX); Assert.Equal(clip.Y, bounds.renderY);
                Assert.Equal(clip.Width, bounds.OuterWidth); Assert.Equal(clip.Height, bounds.OuterHeight); Assert.Equal(true, call.Arguments[1]);
            }
        }
    }
    /// <summary>Independent live/source elements and generated resources survive peer retirement; style changes retain sampled data.</summary>
    [Theory]
    [InlineData("time")] [InlineData("temperature")] [InlineData("date")]
    public void SourceAndStyleGenerationsOwnIndependentResources(string kind)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create(kind, EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        {
            using var live = fixture.Mount(overlay, presentation);
            using var source = fixture.Mount(overlay, presentation, NativeViewRole.SourceTrace);
            Assert.NotSame(Components(live, kind, presentation.Style)[0], Components(source, kind, presentation.Style)[0]);
            var owned = fixture.Resources.Owned.Concat(fixture.Faces).Concat(fixture.Ticks).Concat(fixture.Uploads).Distinct().ToArray();
            live.Dispose(); Assert.Contains(owned, texture => !texture.Disposed);
            source.Composer.Render(0);
            foreach (var style in new[] { EnvironmentalGaugeStyle.Icon, EnvironmentalGaugeStyle.Text, EnvironmentalGaugeStyle.Immersive })
            {
                presentation.SetStyle(style); prepare();
                using var replacement = fixture.Mount(overlay, presentation);
                replacement.Composer.Render(0);
                if (style == EnvironmentalGaugeStyle.Text)
                    Assert.Contains(kind switch { "time" => "04:30", "temperature" => "12°C", _ => "OCT 14" }, fixture.Resources.Values);
            }
            source.Dispose();
            Assert.All(fixture.Resources.Owned.Concat(fixture.Faces).Concat(fixture.Ticks).Concat(fixture.Uploads), texture => Assert.True(texture.Disposed));
            Assert.All(fixture.Resources.Artwork.Where(texture => texture.IgnoreUndisposed), texture => Assert.False(texture.Disposed));
            Assert.Empty(fixture.Resources.Native.Failures);
        }
    }
    /// <summary>Movement updates cached geometry without recreating pixels; context scaling updates only native glyph resources.</summary>
    [Theory]
    [InlineData("time")] [InlineData("temperature")] [InlineData("date")]
    public void MovementAndContextReuseInstrumentArtwork(string kind)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            var element = Components(composition, kind, presentation.Style)[0]; var before = State(element).Target;
            var resources = Counts(fixture);
            composition.Composer.Bounds.fixedX += 17; composition.Composer.Bounds.MarkDirtyRecursive();
            composition.Composer.Bounds.CalcWorldBounds(); composition.View.LayoutCompleted(new(0, 0, 1800, 1800));
            Assert.Equal(before.X + 17, State(element).Target.X, 4); Assert.Equal(resources, Counts(fixture));
            var next = new HudOverlayPreparationContext(fixture.Context.Api, 2, "en", 2);
            composition.View.Prepare(next);
            Assert.Equal(resources.Faces, fixture.Faces.Count); Assert.Equal(resources.Ticks, fixture.Ticks.Count);
            Assert.Equal(resources.Uploads, fixture.Uploads.Count); Assert.Equal(resources.Art, fixture.Resources.Artwork.Count);
        }
    }
    /// <summary>Pointer submission failures restore the matrix and scissor before sibling foreground rendering.</summary>
    [Fact]
    public void PointerFailureRestoresNativeRenderState()
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create("time", EnvironmentalGaugeStyle.Immersive);
        using (overlay)
        using (var composition = fixture.Mount(overlay, presentation))
        {
            int pointer = fixture.Resources.Artwork.First().TextureId;
            fixture.Resources.Native.Render.Setup(value => value.RenderTexture(pointer, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vintagestory.API.MathTools.Vec4f>())).Throws(new InvalidOperationException("submit"));
            composition.Composer.Render(0);
            Assert.Single(fixture.Resources.Native.Failures);
            fixture.Resources.Native.Render.Verify(value => value.GlPopMatrix(), Times.Once());
            Assert.Equal(fixture.Resources.Native.Render.Invocations.Count(call => call.Method.Name == "PushScissor"),
                fixture.Resources.Native.Render.Invocations.Count(call => call.Method.Name == "PopScissor"));
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Finds actual registered native elements in authored mechanical order.</summary>
    private static GuiElement[] Components(IndicatorComposition composition, string kind, EnvironmentalGaugeStyle style)
    {
        string[] keys = style == EnvironmentalGaugeStyle.Text ? ["name", "reading"] : style == EnvironmentalGaugeStyle.Icon ? ["reading", "icon"] : kind switch
        {
            "time" => ["face", "pointer", "glass"],
            "temperature" => ["housing", "empty", "liquid", "ticks", "label0", "label1", "label2", "reading", "clamps", "glass"],
            _ => ["housing", "month", "day", "glass"]
        };
        return keys.Select(key => composition.Composer.GetElement("test:" + key)).ToArray();
    }
    /// <summary>Extracts a component's prepared rendering state without recalculating native bounds.</summary>
    private static ElementState State(GuiElement element) => element switch
    {
        TextureElement texture => texture.State, TextElement text => text.State, FaceElement face => face.State,
        PointerElement pointer => pointer.State, LiquidElement liquid => liquid.State, _ => throw new InvalidOperationException()
    };
    /// <summary>Captures resource and measurement work independently of the chosen visual design.</summary>
    private static (int Glyphs, int Art, int Faces, int Ticks, int Uploads, int Metrics) Counts(InstrumentIndicatorsFixture fixture) =>
        (fixture.Resources.Owned.Count, fixture.Resources.Artwork.Count, fixture.Faces.Count, fixture.Ticks.Count, fixture.Uploads.Count, fixture.Resources.Measurements);
    /// <summary>Filters actual native draw submissions while ignoring state setup and restoration.</summary>
    private static Moq.IInvocation[] Draws(InstrumentIndicatorsFixture fixture) => fixture.Resources.Native.Render.Invocations
        .Where(call => call.Method.Name is "RenderTexture" or "Render2DTexturePremultipliedAlpha").ToArray();
    /// <summary>Allows only floating-point rounding at native rectangle edges.</summary>
    private static bool Contains(RectangleF outer, RectangleF inner) => inner.Left >= outer.Left - .001 && inner.Top >= outer.Top - .001
        && inner.Right <= outer.Right + .001 && inner.Bottom <= outer.Bottom + .001;
    #endregion
}
