using System.Drawing;
using Moq;
using VanillaExpanded.BowAmmunition;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.RiftActivity;
using VanillaExpanded.TemporalStorm;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks migrated consumers through actual native elements, cached geometry and composer ownership.</summary>
[Collection("HudOverlayGeometry")]
public sealed class SimpleIndicatorsTests
{
    #region Public API
    #region Native registration and layout
    /// <summary>Every supported style uses actual components with parented bounds and passive input.</summary>
    [Theory]
    [InlineData("wind", HudIndicatorStyle.Icon, 1)]
    [InlineData("wind", HudIndicatorStyle.Text, 2)]
    [InlineData("rift", HudIndicatorStyle.Icon, 2)]
    [InlineData("rift", HudIndicatorStyle.Text, 1)]
    [InlineData("storm", HudIndicatorStyle.Icon, 1)]
    [InlineData("storm", HudIndicatorStyle.Text, 2)]
    [InlineData("bow", HudIndicatorStyle.Icon, 2)]
    [InlineData("bow", HudIndicatorStyle.Text, 1)]
    public void SupportedStylesComposeRealPassiveComponents(string kind, HudIndicatorStyle style, float scale)
    {
        using var fixture = new SimpleIndicatorsFixture(scale);
        var (overlay, presentation, _, _) = fixture.Create(kind, style);
        using (overlay)
        {
            Assert.Empty(fixture.Owned); Assert.Empty(fixture.Artwork);
            using var view = ViewFactory.Create(overlay, fixture.Native.Api.Object, NativeViewRole.Live, fixture.Native.Failures.Add);
            using var composition = new IndicatorComposition(fixture.Native.Api.Object, view, fixture.Native.Context, presentation.Size,
                new(19, 31, presentation.Size.Width + 40, presentation.Size.Height + 40));
            var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
            if (style == HudIndicatorStyle.Text)
            {
                var name = Assert.IsType<TextElement>(composition.Composer.GetElement("test:name"));
                Assert.Same(name.Bounds.ParentBounds, reading.Bounds.ParentBounds);
                Assert.True(name.State.Target.Bottom <= reading.State.Target.Top + .001);
                Assert.Empty(fixture.Paths); Assert.Empty(fixture.Backdrop.Invocations);
            }
            else
            {
                GuiElement artwork = kind == "bow" ? composition.Composer.GetElement("test:arrow") : composition.Composer.GetElement("test:icon");
                Assert.IsType(kind == "bow" ? typeof(ArrowElement) : typeof(TextureElement), artwork);
                Assert.Same(reading.Bounds.ParentBounds, artwork.Bounds.ParentBounds);
                Assert.True(reading.State.Target.Bottom <= artwork.Bounds.renderY + .001);
                Assert.Equal(reading.Bounds.renderX + reading.Bounds.OuterWidth / 2,
                    artwork.Bounds.renderX + artwork.Bounds.OuterWidth / 2, 4);
                Assert.True(artwork.Bounds.renderY + artwork.Bounds.OuterHeight <= artwork.Bounds.ParentBounds.renderY + artwork.Bounds.ParentBounds.OuterHeight + .001);
            }
            Assert.False(reading.Focusable);
            var press = new MouseEvent(0, 0, EnumMouseButton.Left, 0);
            reading.OnMouseDownOnElement(fixture.Native.Api.Object, press);
            Assert.False(press.Handled);
            // These migrated controllers deliberately reject accidental manual dispatch.
            Assert.Throws<InvalidOperationException>(() => overlay.Draw(fixture.Native.Render.Object, composition.Composer.Bounds, new(0, 0, 900, 900), 0));
            composition.Composer.Render(.1f);
            Assert.Empty(fixture.Native.Failures);
        }
    }
    /// <summary>Repeated drawing consumes cached geometry and creates no text or artwork resources.</summary>
    [Theory]
    [InlineData("wind", HudIndicatorStyle.Icon)] [InlineData("wind", HudIndicatorStyle.Text)]
    [InlineData("rift", HudIndicatorStyle.Icon)] [InlineData("rift", HudIndicatorStyle.Text)]
    [InlineData("storm", HudIndicatorStyle.Icon)] [InlineData("storm", HudIndicatorStyle.Text)]
    [InlineData("bow", HudIndicatorStyle.Icon)] [InlineData("bow", HudIndicatorStyle.Text)]
    public void RenderingDoesNotPrepareOrResolveComponents(string kind, HudIndicatorStyle style)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, style);
        using (overlay)
        using (var view = overlay is INativePresentation native ? native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live) : throw new Exception())
        using (var composition = new IndicatorComposition(fixture.Native.Api.Object, view, fixture.Native.Context, presentation.Size))
        {
            var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
            var target = reading.State.Target;
            int owned = fixture.Owned.Count, paths = fixture.Paths.Count, measurements = fixture.Measurements;
            reading.Bounds.fixedWidth += 77; reading.Bounds.MarkDirtyRecursive();
            for (int frame = 0; frame < 5; frame++)
            {
                ((INativePresentation)overlay).BeginFrame(.1f);
                view.SetOpacity(.6f);
                view.CollectBackdrops(fixture.Native.Render.Object);
                composition.Composer.Render(.1f);
            }
            Assert.Equal(target, reading.State.Target);
            Assert.False(reading.Bounds.Initialized);
            Assert.Equal(owned, fixture.Owned.Count); Assert.Equal(paths, fixture.Paths.Count);
            Assert.Equal(measurements, fixture.Measurements);
        }
    }
    #endregion
    #region Updates and lifetime
    /// <summary>Same-size content updates retain resolved native geometry instead of invalidating bounds.</summary>
    [Theory]
    [InlineData("wind", HudIndicatorStyle.Icon)] [InlineData("wind", HudIndicatorStyle.Text)]
    [InlineData("storm", HudIndicatorStyle.Icon)] [InlineData("bow", HudIndicatorStyle.Text)]
    public void SameSizeUpdatesDoNotDirtyResolvedBounds(string kind, HudIndicatorStyle style)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        using (var composition = new IndicatorComposition(fixture.Native.Api.Object,
            ((INativePresentation)overlay).CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size))
        {
            var reading = Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading"));
            var before = new RectangleF((float)reading.Bounds.renderX, (float)reading.Bounds.renderY,
                (float)reading.Bounds.OuterWidth, (float)reading.Bounds.OuterHeight);
            var size = presentation.Size;
            change(); prepare(); composition.View.Prepare(fixture.Native.Context);
            Assert.Equal(size, presentation.Size);
            Assert.True(reading.Bounds.Initialized); Assert.True(reading.Bounds.ParentBounds.Initialized);
            Assert.Equal(before, new RectangleF((float)reading.Bounds.renderX, (float)reading.Bounds.renderY,
                (float)reading.Bounds.OuterWidth, (float)reading.Bounds.OuterHeight));
        }
    }
    /// <summary>Live and source visuals own unique resources, update together, and retire independently.</summary>
    [Theory]
    [InlineData("wind", HudIndicatorStyle.Icon)] [InlineData("wind", HudIndicatorStyle.Text)]
    [InlineData("rift", HudIndicatorStyle.Icon)] [InlineData("rift", HudIndicatorStyle.Text)]
    [InlineData("storm", HudIndicatorStyle.Icon)] [InlineData("storm", HudIndicatorStyle.Text)]
    [InlineData("bow", HudIndicatorStyle.Icon)] [InlineData("bow", HudIndicatorStyle.Text)]
    public void ContentUpdatesAndIndependentComposersRetainOwnership(string kind, HudIndicatorStyle style)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        {
            var native = Assert.IsAssignableFrom<INativePresentation>(overlay);
            using var live = new IndicatorComposition(fixture.Native.Api.Object,
                native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size);
            int liveResources = fixture.Owned.Count;
            using var source = new IndicatorComposition(fixture.Native.Api.Object,
                native.CreateView(fixture.Native.Api.Object, NativeViewRole.SourceTrace), fixture.Native.Context, presentation.Size);
            Assert.NotSame(live.Composer.GetElement("test:reading"), source.Composer.GetElement("test:reading"));
            var surviving = fixture.Owned.Skip(liveResources).ToArray();
            var original = fixture.Owned.ToArray();
            int count = fixture.Owned.Count;
            live.View.Prepare(fixture.Native.Context); source.View.Prepare(fixture.Native.Context);
            Assert.Equal(count, fixture.Owned.Count);
            change(); prepare();
            live.Update(fixture.Native.Context); source.Update(fixture.Native.Context);
            // A changed reading or selected stage updates existing elements, without replacing their native trees.
            if (style == HudIndicatorStyle.Text)
            {
                Assert.False(original[0].Disposed); Assert.False(original[liveResources].Disposed);
                Assert.True(original[1].Disposed); Assert.True(original[liveResources + 1].Disposed);
            }
            else Assert.All(original.Where(texture => !surviving.Contains(texture)).Where(texture => texture.Width != texture.Height),
                texture => Assert.True(texture.Disposed));
            var survivingNow = fixture.Owned.Skip(count).Where(texture => !texture.Disposed).ToArray();
            live.Dispose();
            source.View.SetOpacity(.5f); source.Composer.Render(.1f);
            if (survivingNow.Length > 0) Assert.Contains(survivingNow, texture => !texture.Disposed);
            source.Dispose();
            Assert.All(fixture.Owned, texture => Assert.True(texture.Disposed));
            if (kind == "wind" && style == HudIndicatorStyle.Icon) Assert.All(fixture.Artwork, texture => Assert.True(texture.Disposed));
            else Assert.All(fixture.Artwork, texture => Assert.False(texture.Disposed));
        }
    }
    /// <summary>Style replacement preserves feature data and leaves resources alive until their composer retires.</summary>
    [Theory]
    [InlineData("wind")] [InlineData("rift")] [InlineData("storm")] [InlineData("bow")]
    public void StyleSwitchReleasesOnlySupersededComposerResources(string kind)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create(kind, HudIndicatorStyle.Icon);
        using (overlay)
        {
            var native = (INativePresentation)overlay;
            using var icon = new IndicatorComposition(fixture.Native.Api.Object,
                native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size);
            var old = fixture.Owned.ToArray();
            Assert.True(presentation.SetStyle(HudIndicatorStyle.Text)); prepare();
            using var text = new IndicatorComposition(fixture.Native.Api.Object,
                native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size);
            Assert.All(old, texture => Assert.False(texture.Disposed));
            Assert.NotNull(text.Composer.GetElement("test:name"));
            icon.Dispose(); Assert.All(old, texture => Assert.True(texture.Disposed));
            text.Composer.Render(0);
            Assert.Empty(fixture.Native.Failures);
        }
    }
    #endregion
    #region Feature semantics
    /// <summary>Rift abbreviation changes select matching borrowed artwork and align the procedural circle.</summary>
    [Theory]
    [InlineData(0, "LOW", "low")]
    [InlineData(1, "MEDIUM", "medium")]
    [InlineData(2, "HIGH", "high")]
    [InlineData(3, "APOCALYPTIC", "apocalyptic")]
    public void RiftLabelsAndBackdropsUsePreparedIconGeometry(int level, string expected, string asset)
    {
        using var fixture = new SimpleIndicatorsFixture(2);
        using var presentation = new RiftActivityPresentation(fixture.Measure, fixture.CreateText, fixture.Load, _ => fixture.Backdrop.Object);
        presentation.SetContent((RiftActivityLevel)level); presentation.Prepare(fixture.Native.Context);
        using var composition = new IndicatorComposition(fixture.Native.Api.Object, presentation.CreateView(fixture.Native.Api.Object), fixture.Native.Context, presentation.Size);
        Assert.Contains(expected, fixture.Values); Assert.EndsWith(asset + ".png", Assert.Single(fixture.Paths));
        var icon = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:icon"));
        composition.View.CollectBackdrops(fixture.Native.Render.Object);
        var queued = Assert.Single(fixture.Backdrop.Invocations);
        Assert.Equal((double)icon.State.Target.X, queued.Arguments[1]); Assert.Equal((double)icon.State.Target.Y, queued.Arguments[2]);
    }
    /// <summary>Wind retains whole percentages above one hundred in both native styles.</summary>
    [Theory]
    [InlineData(HudIndicatorStyle.Icon)] [InlineData(HudIndicatorStyle.Text)]
    public void WindUsesActualPercentageAndCachesUnchangedCharacters(HudIndicatorStyle style)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, prepare, change) = fixture.Create("wind", style);
        using (overlay)
        using (var composition = new IndicatorComposition(fixture.Native.Api.Object,
            ((INativePresentation)overlay).CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size))
        {
            Assert.Contains("125%", fixture.Values);
            int before = fixture.Values.Count;
            prepare(); composition.View.Prepare(fixture.Native.Context);
            Assert.Equal(before, fixture.Values.Count);
            change(); prepare(); composition.Update(fixture.Native.Context);
            Assert.Contains("126%", fixture.Values);
        }
    }
    /// <summary>Empty ammunition retains muted zero-state text and prepares no item background or item draw.</summary>
    [Fact]
    public void NoArrowsStateUsesNativeTextOnly()
    {
        using var fixture = new SimpleIndicatorsFixture();
        using var presentation = new BowAmmunitionPresentation(fixture.Measure, fixture.CreateText, fixture.CreateBackground);
        presentation.SetContent(new(null, 0)); presentation.Prepare(fixture.Native.Context);
        using var composition = new IndicatorComposition(fixture.Native.Api.Object, presentation.CreateView(fixture.Native.Api.Object), fixture.Native.Context, presentation.Size);
        composition.Composer.Render(.1f);
        Assert.Contains("No arrows / 0", fixture.Values); Assert.Single(fixture.Owned);
        Assert.DoesNotContain(fixture.Native.Render.Invocations, call => call.Method.Name == "RenderItemstackToGui");
    }
    /// <summary>Returning arrows restore their text treatment and replace background resources released by the empty state.</summary>
    [Fact]
    public void EmptyArrowTransitionRestoresNormalTextAndCircleOwnership()
    {
        using var fixture = new SimpleIndicatorsFixture();
        var colors = new List<double[]>();
        using var presentation = new BowAmmunitionPresentation(fixture.Measure,
            (context, text, font) => { colors.Add(font.Color.ToArray()); return fixture.CreateText(context, text, font); }, fixture.CreateBackground);
        var arrow = new Item { Code = new AssetLocation("game:test-arrow") };
        presentation.SetContent(new(new ItemStack(arrow), 12)); presentation.Prepare(fixture.Native.Context);
        using var composition = new IndicatorComposition(fixture.Native.Api.Object, presentation.CreateView(fixture.Native.Api.Object), fixture.Native.Context, presentation.Size);
        var oldCircle = fixture.Owned[^1];
        presentation.SetContent(new(null, 0)); presentation.Prepare(fixture.Native.Context); composition.Update(fixture.Native.Context);
        Assert.True(oldCircle.Disposed);
        presentation.SetContent(new(new ItemStack(arrow), 12)); presentation.Prepare(fixture.Native.Context); composition.Update(fixture.Native.Context);
        Assert.Equal(colors[0], colors[2]);
        Assert.NotSame(oldCircle, fixture.Owned[^1]); Assert.False(fixture.Owned[^1].Disposed);
    }
    /// <summary>Each composer renders a separate mutable arrow copy and shares fade across count, circle and item.</summary>
    [Fact]
    public void ArrowRenderingNeverMutatesSampleOrAnotherComposerCopy()
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create("bow", HudIndicatorStyle.Icon);
        using (overlay)
        {
            var bow = Assert.IsType<BowAmmunitionPresentation>(presentation);
            var native = (INativePresentation)overlay;
            using var live = new IndicatorComposition(fixture.Native.Api.Object, native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size);
            using var source = new IndicatorComposition(fixture.Native.Api.Object, native.CreateView(fixture.Native.Api.Object, NativeViewRole.SourceTrace), fixture.Native.Context, presentation.Size);
            var copies = new List<ItemStack>();
            fixture.Native.Render.Setup(renderer => renderer.RenderItemstackToGui(It.IsAny<ItemSlot>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<float>(), It.IsAny<int>(), It.IsAny<float>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback<ItemSlot, double, double, double, float, int, float, bool, bool, bool>((slot, _, _, _, _, _, _, _, _, _) =>
                { copies.Add(slot.Itemstack!); slot.Itemstack!.StackSize = 99; });
            live.View.SetOpacity(.4f); source.View.SetOpacity(.4f);
            live.Composer.Render(.1f); source.Composer.Render(0);
            Assert.Equal(2, copies.Count); Assert.NotSame(copies[0], copies[1]); Assert.NotSame(bow.Icon, copies[0]);
            Assert.Equal(12, bow.Icon!.StackSize);
            var itemCalls = fixture.Native.Render.Invocations.Where(call => call.Method.Name == "RenderItemstackToGui").ToArray();
            Assert.Equal(itemCalls[0].Arguments[5], itemCalls[1].Arguments[5]);
            var reading = Assert.IsType<TextElement>(live.Composer.GetElement("test:reading"));
            Assert.Equal(.4f, reading.State.Opacity);
            Assert.Empty(fixture.Native.Failures);
        }
    }
    /// <summary>Storm rendering never advances the shared pulse; both styles and source copies see the same frame.</summary>
    [Theory]
    [InlineData(HudIndicatorStyle.Icon)] [InlineData(HudIndicatorStyle.Text)]
    public void StormPulseAdvancesOnceAndPreservesPhaseAcrossUrgencyAndStyle(HudIndicatorStyle style)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create("storm", style);
        using (overlay)
        {
            var storm = Assert.IsType<TemporalStormPresentation>(presentation);
            var native = (INativePresentation)overlay;
            using var live = new IndicatorComposition(fixture.Native.Api.Object, native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size);
            using var source = new IndicatorComposition(fixture.Native.Api.Object, native.CreateView(fixture.Native.Api.Object, NativeViewRole.SourceTrace), fixture.Native.Context, presentation.Size);
            native.BeginFrame(.5f); live.View.SetOpacity(1); source.View.SetOpacity(1);
            float first = ForegroundOpacity(live, style), shared = ForegroundOpacity(source, style);
            Assert.Equal(first, shared); Assert.True(first < 1);
            for (int frame = 0; frame < 3; frame++) { live.Composer.Render(.5f); source.Composer.Render(.5f); }
            live.View.SetOpacity(1); Assert.Equal(first, ForegroundOpacity(live, style));
            storm.SetContent(TemporalStormStage.Imminent, 0); native.BeginFrame(0); live.View.SetOpacity(1);
            Assert.Equal(first, ForegroundOpacity(live, style));
            native.BeginFrame(1f / 12); live.View.SetOpacity(1);
            Assert.True(ForegroundOpacity(live, style) < first);
            // A style switch does not reset the pulse, and active storms pulse only when waning.
            float prior = ForegroundOpacity(live, style);
            var alternate = style == HudIndicatorStyle.Icon ? HudIndicatorStyle.Text : HudIndicatorStyle.Icon;
            presentation.SetStyle(alternate); prepare(); native.BeginFrame(0);
            using var replacement = new IndicatorComposition(fixture.Native.Api.Object, native.CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size);
            Assert.Equal(prior, ForegroundOpacity(replacement, alternate));
            storm.SetContent(TemporalStormStage.Active, .02); native.BeginFrame(.1f); replacement.View.SetOpacity(1);
            Assert.Equal(1, ForegroundOpacity(replacement, alternate));
            storm.SetContent(TemporalStormStage.Active, .0199, waning: true); native.BeginFrame(.5f); replacement.View.SetOpacity(1);
            Assert.True(ForegroundOpacity(replacement, alternate) < 1);
        }
    }
    /// <summary>Native backdrop submissions carry the host fade while the storm's foreground pulse remains independent.</summary>
    [Theory]
    [InlineData("rift")] [InlineData("storm")]
    public void BackdropFadeIsSharedWithoutApplyingForegroundPulse(string kind)
    {
        using var fixture = new SimpleIndicatorsFixture();
        var (overlay, presentation, _, _) = fixture.Create(kind, HudIndicatorStyle.Icon);
        using (overlay)
        using (var composition = new IndicatorComposition(fixture.Native.Api.Object,
            ((INativePresentation)overlay).CreateView(fixture.Native.Api.Object, NativeViewRole.Live), fixture.Native.Context, presentation.Size))
        {
            float queued = 0;
            fixture.Backdrop.Setup(backdrop => backdrop.Queue(It.IsAny<IRenderAPI>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
                .Callback(() => queued = VanillaExpanded.HudOverlays.Rendering.DrawOpacity.Current);
            ((INativePresentation)overlay).BeginFrame(.5f);
            composition.View.SetOpacity(.6f); composition.View.CollectBackdrops(fixture.Native.Render.Object);
            Assert.Equal(.6f, queued);
            var icon = Assert.IsType<TextureElement>(composition.Composer.GetElement("test:icon"));
            if (kind == "storm") Assert.True(icon.State.Opacity < queued);
            else Assert.Equal(queued, icon.State.Opacity);
        }
    }
    /// <summary>Urgency-only samples update the shared storm effect even when stage equality suppresses resource invalidation.</summary>
    [Fact]
    public void UrgencyOnlyRefreshChangesPulseWithoutRepreparingComponents()
    {
        using var resources = new SimpleIndicatorsFixture();
        var fixture = new VanillaExpanded.Tests.Unit.BowAmmunition.BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        fixture.Api.SetupGet(api => api.Render).Returns(resources.Native.Render.Object);
        fixture.Api.SetupGet(api => api.Gui).Returns(resources.Native.Gui.Object);
        fixture.Api.SetupGet(api => api.Input).Returns(resources.Native.Api.Object.Input);
        var storms = new Vintagestory.GameContent.SystemTemporalStability();
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>(true)).Returns(storms);
        fixture.Api.SetupGet(api => api.ModLoader).Returns(loader.Object);
        var calendar = new Mock<IClientGameCalendar>(); calendar.SetupGet(value => value.TotalDays).Returns(0);
        fixture.World.SetupGet(world => world.Calendar).Returns(calendar.Object);
        var settings = new Vintagestory.API.Datastructures.TreeAttribute();
        settings.SetBool("temporalStability", true); settings.SetString("temporalStorms", "normal");
        fixture.World.SetupGet(world => world.Config).Returns(settings);
        using var presentation = new TemporalStormPresentation(resources.Measure, resources.CreateText, resources.Load, _ => resources.Backdrop.Object);
        using var overlay = new TemporalStormOverlay(presentation);
        var context = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en");
        overlay.BeginSession(fixture.Api.Object); storms.StormData.nextStormTotalDays = .02;
        overlay.Refresh(); overlay.Prepare(context);
        using var composition = new IndicatorComposition(fixture.Api.Object, overlay.CreateView(fixture.Api.Object, NativeViewRole.Live), context, overlay.Measure());
        overlay.BeginFrame(.5f); composition.View.SetOpacity(1);
        float previous = ForegroundOpacity(composition, HudIndicatorStyle.Icon);
        int creates = resources.Owned.Count, paths = resources.Paths.Count;
        storms.StormData.nextStormTotalDays = 0;
        Assert.Equal(HudOverlayChange.None, overlay.Refresh());
        overlay.BeginFrame(0); composition.View.SetOpacity(1);
        Assert.Equal(previous, ForegroundOpacity(composition, HudIndicatorStyle.Icon));
        overlay.BeginFrame(1f / 12); composition.View.SetOpacity(1);
        Assert.True(ForegroundOpacity(composition, HudIndicatorStyle.Icon) < previous);
        Assert.Equal(creates, resources.Owned.Count); Assert.Equal(paths, resources.Paths.Count);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reads settled component state rather than asserting a prescribed pulse brightness.</summary>
    private static float ForegroundOpacity(IndicatorComposition composition, HudIndicatorStyle style) =>
        style == HudIndicatorStyle.Icon ? Assert.IsType<TextureElement>(composition.Composer.GetElement("test:icon")).State.Opacity
        : Assert.IsType<TextElement>(composition.Composer.GetElement("test:reading")).State.Opacity;
    #endregion
}
