using Cairo;
using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks hover eligibility without composing text or issuing graphics calls.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayHoverLabelTests : IDisposable
{
    private readonly string previousLocale = Lang.CurrentLocale;
    private readonly ITranslationService? previousLanguage;

    #region Public API
    /// <summary>Provides native localization without loading game assets.</summary>
    public HudOverlayHoverLabelTests()
    {
        Lang.AvailableLanguages.TryGetValue("en", out previousLanguage);
        Lang.AvailableLanguages["en"] = Mock.Of<ITranslationService>();
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
    }

    /// <summary>Restores shared localization after each test.</summary>
    public void Dispose()
    {
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, previousLocale);
        if (previousLanguage == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = previousLanguage;
    }

    /// <summary>Labels use the foreground pass above passive HUDs while respecting native window and visibility ownership.</summary>
    [Theory]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, true, false, true, false)]
    public void HoverLabelsUseForegroundPassWithoutCoveringWindowTargets(bool hud, bool modal, bool overlaps,
        bool hidden, bool grabbed, bool expected)
    {
        using var native = new HudNativeComponentFixture();
        var api = native.Api;
        var input = Mock.Get(api.Object.Input);
        input.SetupGet(value => value.MouseGrabbed).Returns(grabbed);
        api.SetupGet(value => value.HideGuis).Returns(hidden);
        native.Gui.SetupGet(value => value.LoadedGuis).Returns(new List<GuiDialog>());
        using var registry = new HudOverlayRegistry();
        var overlay = new Mock<IHudOverlay>();
        overlay.SetupGet(value => value.HoverValue).Returns("42%");
        overlay.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>()))
            .Returns(() => new CallbackView(api.Object));
        var registration = new HudOverlayRegistration("test:label", overlay.Object, () => true, "group",
            hoverLabelKey: "test:hover");
        var definition = new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        registry.RegisterGroup(definition); registry.Register(registration);
        using var group = new HudOverlayGroupLayout();
        var window = ElementBounds.Fixed(0, 0, 700, 500).WithEmptyParent(); window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => 1, () => null); anchors.BeginFrame();
        group.Apply(anchors, definition, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new(40, 30)) });
        var pointer = group.Members.Single().Clip;
        input.SetupGet(value => value.MouseX).Returns((int)pointer.Left + 1);
        input.SetupGet(value => value.MouseY).Returns((int)pointer.Top + 1);
        native.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { api.Object, bounds, name }, null)!);
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, error) => throw error);
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { ["group"] = group }); host.TryOpen(false);
        var foreground = new Mock<GuiDialog>(api.Object);
        foreground.Setup(value => value.IsOpened()).Returns(true);
        foreground.Setup(value => value.CaptureAllInputs()).Returns(modal);
        foreground.SetupGet(value => value.DialogType).Returns(hud ? EnumDialogType.HUD : EnumDialogType.Dialog);
        var bounds = ElementBounds.Fixed(overlaps ? pointer.Left : pointer.Right + 100, pointer.Top, 60, 60).WithParent(window);
        bounds.CalcWorldBounds();
        foreground.Object.Composers["peer"] = api.Object.Gui.CreateCompo("peer", bounds);
        native.Gui.SetupGet(value => value.OpenedGuis).Returns(new List<GuiDialog> { host, foreground.Object });
        var labels = (HudOverlayHoverLabels)typeof(HudOverlayHost).GetField("hoverLabels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var tooltip = new Mock<GuiElementHoverText>(api.Object, "label", CairoFont.WhiteSmallText(), 280,
            ElementBounds.Fixed(0, 0, 1, 1).WithEmptyParent(), null!);
        typeof(HudOverlayHoverLabels).GetField("tooltip", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(labels, tooltip.Object);
        typeof(HudOverlayHoverLabels).GetField("text", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(labels,
            HudOverlayHoverLabels.ResolveText(registration, Lang.Get(registration.HoverLabelKey!)));
        typeof(HudOverlayHoverLabels).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(labels, (double)RuntimeEnv.GUIScale);
        var artwork = (GuiDialog)typeof(HudOverlayHost).GetField("artworkDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Assert.True(artwork.DrawOrder < host.DrawOrder);
        artwork.OnRenderGUI(.1f);
        tooltip.Verify(value => value.RenderInteractiveElements(It.IsAny<float>()), Times.Never);
        native.Render.Invocations.Clear();
        host.OnRenderGUI(.1f);
        tooltip.Verify(value => value.RenderInteractiveElements(.1f), expected ? Times.Once() : Times.Never());
        Assert.DoesNotContain(native.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.GlTranslate));
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("42%", false, "Indicator\n42%")]
    [InlineData(null, false, "Indicator\nUnavailable")]
    [InlineData("", false, "Indicator\nUnavailable")]
    [InlineData(" ", false, "Indicator\nUnavailable")]
    [InlineData("stale reading", true, "Indicator (inactive)\nUnavailable")]
    public void TooltipHasValueOnSecondLineWithoutReadingInactiveState(string? value, bool inactive, string expected)
    {
        Mock.Get(Lang.AvailableLanguages["en"])
            .Setup(language => language.Get("vanillaexpanded:hud-value-unavailable", It.IsAny<object[]>()))
            .Returns("Unavailable");
        var overlay = new Mock<IHudOverlay>();
        overlay.SetupGet(feature => feature.HoverValue).Returns(value);
        var registration = new HudOverlayRegistration("test:value", overlay.Object, () => true, "group");

        Assert.Equal(expected, HudOverlayHoverLabels.ResolveText(registration, inactive ? "Indicator (inactive)" : "Indicator", inactive));
        overlay.VerifyGet(feature => feature.HoverValue, inactive ? Times.Never() : Times.Once());
        overlay.Verify(feature => feature.Refresh(), Times.Never());
        overlay.Verify(feature => feature.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never());
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("shelter", "READING", "READING")]
    [InlineData("wind", "125%", "126%")]
    [InlineData("rift", "HIGH", "LOW")]
    [InlineData("storm", "IMMINENT", "ACTIVE")]
    [InlineData("bow", "12", "8")]
    public void SimpleIndicatorValuesAreStyleIndependentAndChangeWithPreparation(string kind, string expected, string changed)
    {
        foreach (var style in new[] { HudIndicatorStyle.Icon, HudIndicatorStyle.Text })
        {
            using var fixture = new SimpleIndicatorsFixture();
            var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
            using (overlay)
            {
                Assert.Equal(expected, overlay.HoverValue);
                int measurements = fixture.Measurements;
                string? cached = overlay.HoverValue;
                for (int frame = 0; frame < 5; frame++) Assert.Same(cached, overlay.HoverValue);
                Assert.Equal(measurements, fixture.Measurements);
                Assert.Empty(fixture.Owned); Assert.Empty(fixture.Artwork);
                change(); prepare();
                Assert.Equal(changed, overlay.HoverValue);
                presentation.Reset();
                Assert.Null(overlay.HoverValue);
            }
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("wetness", "26%", "27%")]
    [InlineData("body", "34\u00b0C", "34\u00b0C")]
    [InlineData("weather", "RAIN2 FOG2 THR1", "SNOW3 FOG1 THR2")]
    public void LayeredIndicatorValuesAreStyleIndependentAndChangeWithPreparation(string kind, string expected, string changed)
    {
        foreach (var style in new[] { HudIndicatorStyle.Icon, HudIndicatorStyle.Text })
        {
            using var fixture = new LayeredIndicatorsFixture();
            var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
            using (overlay)
            {
                Assert.Equal(expected, overlay.HoverValue);
                int measurements = fixture.Measurements, packChecks = fixture.PackChecks;
                string? cached = overlay.HoverValue;
                for (int frame = 0; frame < 5; frame++) Assert.Same(cached, overlay.HoverValue);
                Assert.Equal(measurements, fixture.Measurements); Assert.Equal(packChecks, fixture.PackChecks);
                Assert.Empty(fixture.Glyphs); Assert.Empty(fixture.Bindings);
                change(); prepare();
                Assert.Equal(changed, overlay.HoverValue);
                presentation.Reset();
                Assert.Null(overlay.HoverValue);
            }
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("time", "04:30", "04:30")]
    [InlineData("temperature", "12\u00b0C", "12\u00b0C")]
    [InlineData("date", "OCT 14", "OCT 15")]
    public void InstrumentValuesAreStyleIndependentAndChangeWithPreparation(string kind, string expected, string changed)
    {
        foreach (var style in new[] { EnvironmentalGaugeStyle.Immersive, EnvironmentalGaugeStyle.Icon, EnvironmentalGaugeStyle.Text })
        {
            using var fixture = new InstrumentIndicatorsFixture();
            var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
            using (overlay)
            {
                Assert.Equal(expected, overlay.HoverValue);
                int measurements = fixture.Resources.Measurements;
                string? cached = overlay.HoverValue;
                for (int frame = 0; frame < 5; frame++) Assert.Same(cached, overlay.HoverValue);
                Assert.Equal(measurements, fixture.Resources.Measurements);
                Assert.Empty(fixture.Resources.Owned); Assert.Empty(fixture.Faces); Assert.Empty(fixture.Ticks);
                change(); prepare();
                Assert.Equal(changed, overlay.HoverValue);
                presentation.Reset();
                Assert.Null(overlay.HoverValue);
            }
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(HudIndicatorStyle.Icon)]
    [InlineData(HudIndicatorStyle.Text)]
    public void BodyTemperatureTooltipKeepsCelsiusWhenOnIndicatorReadingIsDisabled(HudIndicatorStyle style)
    {
        using var fixture = new LayeredIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create("body", style);
        using (overlay)
        {
            var thermometer = Assert.IsType<BodyTemperatureThermometerPresentation>(presentation);
            thermometer.SetContent(new(35, 37, BodyTemperatureRisk.Cold), false);
            prepare();
            Assert.Empty(thermometer.Reading);
            Assert.Equal("35\u00b0C", overlay.HoverValue);
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(EnvironmentalGaugeStyle.Immersive)]
    [InlineData(EnvironmentalGaugeStyle.Icon)]
    [InlineData(EnvironmentalGaugeStyle.Text)]
    public void ClockTooltipUpdatesWhenOnlyActualClockTimeChanges(EnvironmentalGaugeStyle style)
    {
        using var fixture = new InstrumentIndicatorsFixture();
        var (overlay, presentation, prepare, _) = fixture.Create("time", style);
        using (overlay)
        {
            var time = Assert.IsType<ApproximateTimePresentation>(presentation);
            time.SetContent(ApproximateTimePeriod.Noon, .5, actualHour: 4.52);
            prepare();
            Assert.Equal("04:31", overlay.HoverValue);
        }
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeTooltipComposesTwoLinesAndCachesUntilItsValueChanges(float scale)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        fixture.Render.SetupGet(render => render.ScissorStack).Returns(new Stack<ElementBounds>());
        fixture.Render.SetupGet(render => render.FrameWidth).Returns(1000);
        fixture.Render.SetupGet(render => render.FrameHeight).Returns(800);
        int uploads = 0;
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new CairoUpload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                texture.TextureId = ++uploads;
                texture.Width = surface.Width; texture.Height = surface.Height;
            }));
        Mock.Get(Lang.AvailableLanguages["en"])
            .Setup(language => language.Get("vanillaexpanded:hud-value-unavailable", It.IsAny<object[]>()))
            .Returns("Unavailable");
        string value = "125%";
        var overlay = new Mock<IHudOverlay>();
        overlay.SetupGet(feature => feature.HoverValue).Returns(() => value);
        var registration = new HudOverlayRegistration("test:wind", overlay.Object, () => true, "group", hoverLabelKey: "test:wind-label");
        using var group = new HudOverlayGroupLayout();
        var window = ElementBounds.Fixed(0, 0, 800, 600).WithEmptyParent(); window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => scale, () => null); anchors.BeginFrame();
        group.Apply(anchors, new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)),
            new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new(40, 30)) });
        var clip = group.Members.Single().Clip;
        var input = Mock.Get(fixture.Api.Object.Input);
        input.SetupGet(pointer => pointer.MouseX).Returns((int)clip.Left + 1);
        input.SetupGet(pointer => pointer.MouseY).Returns((int)clip.Top + 1);

        using var singleLine = new GuiElementHoverText(fixture.Api.Object, "Wind Speed", CairoFont.WhiteSmallText(), 280,
            ElementBounds.Fixed(0, 0, 1, 1).WithEmptyParent());
        singleLine.SetVisible(true); singleLine.RenderInteractiveElements(.1f);
        double singleLineHeight = Description(singleLine).Bounds.fixedHeight;
        using var labels = new HudOverlayHoverLabels(fixture.Api.Object);
        bool inactive = false;
        void Draw() => labels.Draw(new[] { group }, _ => true, .1f,
            _ => inactive ? "Wind Speed (inactive)" : "Wind Speed", _ => inactive);
        GuiElementHoverText Tooltip() => (GuiElementHoverText)typeof(HudOverlayHoverLabels)
            .GetField("tooltip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(labels)!;
        string Text() => (string)typeof(HudOverlayHoverLabels)
            .GetField("text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(labels)!;

        Draw();
        var first = Tooltip();
        Assert.Equal("Wind Speed\n125%", Text());
        Assert.True(Description(first).Bounds.fixedHeight > singleLineHeight);
        int cachedUploads = uploads;
        for (int frame = 0; frame < 5; frame++) Draw();
        Assert.Same(first, Tooltip()); Assert.Equal(cachedUploads, uploads);
        value = "126%"; Draw();
        Assert.NotSame(first, Tooltip()); Assert.True(uploads > cachedUploads);
        Assert.Equal("Wind Speed\n126%", Text());
        inactive = true; overlay.Invocations.Clear(); Draw();
        Assert.Equal("Wind Speed (inactive)\nUnavailable", Text());
        overlay.VerifyGet(feature => feature.HoverValue, Times.Never());
        cachedUploads = uploads; Draw();
        Assert.Equal(cachedUploads, uploads);
    }

    private static GuiElementRichtext Description(GuiElementHoverText tooltip) => (GuiElementRichtext)typeof(GuiElementHoverText)
        .GetField("descriptionElement", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tooltip)!;

    private delegate void CairoUpload(ImageSurface surface, bool linear, ref LoadedTexture texture);

    /// <summary>Only a labeled visible member under a free pointer exposes a hover target.</summary>
    [Theory]
    [InlineData(false, true, true, true, 25, 25, 20, true)]
    [InlineData(true, true, true, true, 25, 25, 20, false)]
    [InlineData(false, false, true, true, 25, 25, 20, false)]
    [InlineData(false, true, false, true, 25, 25, 20, false)]
    [InlineData(false, true, true, false, 25, 25, 20, false)]
    [InlineData(false, true, true, true, 50, 20, 20, false)]
    [InlineData(false, true, true, true, 25, 25, 0, false)]
    public void HitTestingRequiresVisibleLabeledMember(bool grabbed, bool available, bool drawable,
        bool labeled, float x, float y, float width, bool expected)
    {
        float oldScale = RuntimeEnv.GUIScale;
        int oldLeft = GuiStyle.LeftDialogMargin;
        try
        {
            RuntimeEnv.GUIScale = 1;
            GuiStyle.LeftDialogMargin = 0;
            var registration = new HudOverlayRegistration("test:indicator", Mock.Of<IHudOverlay>(), () => true, "group",
                hoverLabelKey: labeled ? "test:label" : null);
            using var group = new HudOverlayGroupLayout();
            var window = ElementBounds.Fixed(0, 0, 200, 100).WithEmptyParent();
            window.CalcWorldBounds();
            var anchors = new HudOverlayAnchorContext(window, () => 1, () => null);
            anchors.BeginFrame();
            group.Apply(anchors, new HudOverlayGroup("group", new HudOverlayPlacement(available ? "screen" : "hotbar",
                HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, 5, 5)),
                new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(width, 20)) });
            var result = HudOverlayHoverLabels.FindTarget(new[] { group }, _ => drawable, grabbed, new PointF(x, y));
            Assert.Equal(expected, result == registration);
        }
        finally
        {
            RuntimeEnv.GUIScale = oldScale;
            GuiStyle.LeftDialogMargin = oldLeft;
        }
    }
    #endregion
}
