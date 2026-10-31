using System.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.HudOverlays.Updating;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

[Collection("HudOverlayGeometry")]
[Trait("Category", "Unit")]
public sealed class HudIndicatorSizeTests
{
    [Theory]
    [InlineData("BowAmmunition", 24)] [InlineData("BodyTemperature", 24)] [InlineData("Shelter", 24)]
    [InlineData("PlayerWetness", 24)] [InlineData("WeatherConditions", 36)] [InlineData("WindSpeed", 24)]
    [InlineData("RiftActivity", 24)] [InlineData("TemporalStorm", 24)] [InlineData("ApproximateTime", 90)]
    [InlineData("EnvironmentalTemperature", 90)] [InlineData("CalendarDate", 96)]
    public void DefaultsMigrationPersistenceAndConfigLibStayIndependent(string feature, int expected)
    {
        string name = feature + "IndicatorSize";
        var property = typeof(VanillaExpandedConfig).GetProperty(name)!;
        var config = JsonConvert.DeserializeObject<VanillaExpandedConfig>("{}")!;
        Assert.Equal(expected, property.GetValue(config));
        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, new StringAttribute("{\"" + name + "\":\"42\"}"));
        Assert.Equal(1, applied); Assert.Equal(42, property.GetValue(config));
        var roundTrip = JsonConvert.DeserializeObject<VanillaExpandedConfig>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(42, property.GetValue(roundTrip));
        var defaults = new VanillaExpandedConfig();
        foreach (var other in typeof(VanillaExpandedConfig).GetProperties().Where(value => value.Name.EndsWith("IndicatorSize") && value != property))
            Assert.Equal(other.GetValue(defaults), other.GetValue(config));

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VanillaExpanded.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var settings = JObject.Parse(File.ReadAllText(Path.Combine(directory.FullName, "VanillaExpanded", "assets", "vanillaexpanded", "config", "configlib-patches.json")));
        var entry = settings["settings"]!["integer"]![name]!;
        Assert.Equal(expected, entry["default"]!.Value<int>());
        Assert.Equal(6, entry["range"]!["min"]!.Value<int>());
        Assert.Equal(300, entry["range"]!["max"]!.Value<int>());
        Assert.Equal(2, entry["range"]!["step"]!.Value<int>());
        Assert.True(entry["clientSide"]!.Value<bool>());
        var language = JObject.Parse(File.ReadAllText(Path.Combine(directory.FullName, "VanillaExpanded", "assets", "vanillaexpanded", "lang", "en.json")));
        Assert.NotNull(language[entry["ingui"]!.Value<string>()!.Split(':')[1]]);
        Assert.NotNull(language[entry["comment"]!.Value<string>()!.Split(':')[1]]);
    }

    [Theory]
    [InlineData(int.MinValue, 6)] [InlineData(0, 6)] [InlineData(6, 6)] [InlineData(8, 8)]
    [InlineData(9, 10)] [InlineData(24, 24)] [InlineData(26, 26)] [InlineData(27, 28)] [InlineData(299, 300)]
    [InlineData(300, 300)] [InlineData(int.MaxValue, 300)]
    public void PersistedValuesUseSafeTwoUnitSteps(int raw, int expected)
    {
        Assert.Equal(expected, HudOverlaySizeSetting.Normalize(raw));
        var config = new VanillaExpandedConfig();
        foreach (var property in typeof(VanillaExpandedConfig).GetProperties().Where(value => value.Name.EndsWith("IndicatorSize") && value.PropertyType == typeof(int)))
        {
            property.SetValue(config, raw);
            Assert.Equal(expected, property.GetValue(config));
        }
    }

    [Fact]
    public void FailedSizeSaveRollsBack()
    {
        int value = 24;
        var setting = new HudOverlaySizeSetting(24, () => value, next => value = next, () => throw new IOException("save failed"));
        Assert.Throws<IOException>(() => setting.Set(30));
        Assert.Equal(24, setting.Value);
    }

    [Fact]
    public void LiveHostSizeEditsResizeExistingViewsWithoutResamplingOrRecomposing()
    {
        int size = 24;
        var setting = new HudOverlaySizeSetting(24, () => size, value => size = value, () => { });
        using var host = new HudNativeHostFixture(new HudOverlayMenuSettings(Size: setting));
        host.Synchronize(); host.Render();
        var composer = host.Host.Composers[host.Group.Id];
        var icon = Assert.IsType<TextureElement>(composer.GetElement(host.Registration.Id + ":icon"));
        var reading = Assert.IsType<TextElement>(composer.GetElement(host.Registration.Id + ":reading"));
        var originalIcon = icon.State.Target; var originalReading = reading.State.Target;
        int composes = host.Composes;
        foreach (int next in new[] { 48, 6, 30, 24 })
        {
            size = next; double scale = next / 24d;
            host.Size = setting.Apply(new(100, 90)); host.Synchronize(); host.Render();
            Assert.Same(composer, host.Host.Composers[host.Group.Id]);
            Assert.Equal(originalIcon.Width * scale, icon.State.Target.Width, 3);
            Assert.Equal(originalIcon.Height * scale, icon.State.Target.Height, 3);
            AssertClose(originalReading.Width * scale, reading.State.Target.Width);
            AssertClose(originalReading.Height * scale, reading.State.Target.Height);
            Assert.Equal(composes, host.Composes);
            Assert.Empty(host.Failures);
        }
        host.Overlay.Verify(value => value.Refresh(), Times.Never);
        host.Overlay.Verify(value => value.Measure(), Times.Never);
    }

    [Fact]
    public void SchedulerScalesPackingWithoutChangingCanonicalMeasurementsOrSampling()
    {
        using var components = new HudNativeComponentFixture();
        using var registry = new HudOverlayRegistry();
        registry.RegisterGroup(new("test", new("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
        int size = 24;
        var setting = new HudOverlaySizeSetting(24, () => size, value => size = value, () => { });
        var overlay = new Mock<IHudOverlay>();
        overlay.Setup(value => value.IsApplicable()).Returns(true);
        overlay.Setup(value => value.Measure()).Returns(new SizeF(24, 42));
        var registration = new HudOverlayRegistration("test:sized", overlay.Object, () => true, "test",
            new HudOverlayMenuSettings(Size: setting));
        registry.Register(registration); registry.BeginSession(components.Api.Object);
        var window = ElementBounds.Fixed(0, 0, 1000, 1000).WithEmptyParent(); window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => 1, () => null); anchors.BeginFrame();
        var scheduler = new HudOverlayScheduler(registry);
        scheduler.Update(anchors, components.Context, true);
        Assert.Equal(new SizeF(24, 42), Assert.Single(scheduler.GetMembers("test")).Value);
        size = 48;
        Assert.Equal(new SizeF(48, 84), Assert.Single(scheduler.GetMembers("test")).Value);
        overlay.Verify(value => value.Measure(), Times.Once);
        overlay.Verify(value => value.Refresh(), Times.Once);
    }

    public static IEnumerable<object[]> SimpleSizes()
    {
        foreach (string kind in new[] { "wind", "rift", "storm", "bow", "shelter" })
        foreach (HudIndicatorStyle style in Enum.GetValues<HudIndicatorStyle>())
        foreach (int size in new[] { 6, 24, 30, 48, 300 })
            yield return new object[] { kind, style, size };
    }

    [Theory]
    [MemberData(nameof(SimpleSizes))]
    public void SimpleArtworkReadingsAndBackdropsScaleTogether(string kind, HudIndicatorStyle style, int size)
    {
        using var fixture = new SimpleIndicatorsFixture(1.5f);
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        {
            int current = size;
            var setting = new HudOverlaySizeSetting(24, () => current, value => current = value, () => { });
            using var baseline = new IndicatorComposition(fixture.Native.Api.Object, overlay.CreateView(fixture.Native.Api.Object, NativeViewRole.Live),
                fixture.Native.Context, presentation.Size);
            using var resized = new IndicatorComposition(fixture.Native.Api.Object,
                new SizedNativeView(overlay.CreateView(fixture.Native.Api.Object, NativeViewRole.SourceTrace), setting),
                fixture.Native.Context, setting.Apply(presentation.Size));
            AssertScaled(baseline, resized, setting.Scale);
            if (style == HudIndicatorStyle.Icon && kind is "rift" or "storm" or "shelter")
            {
                resized.View.CollectBackdrops(fixture.Native.Render.Object);
                var call = Assert.Single(fixture.Backdrop.Invocations);
                Assert.Equal(1.5 * size / 36, Assert.IsType<double>(call.Arguments[3]), 5);
            }
            change(); prepare(); baseline.Update(fixture.Native.Context); resized.Update(fixture.Native.Context);
            AssertScaled(baseline, resized, setting.Scale);
            int owned = fixture.Owned.Count, loads = fixture.Paths.Count;
            resized.Update(fixture.Native.Context);
            Assert.Equal(owned, fixture.Owned.Count); Assert.Equal(loads, fixture.Paths.Count);
            current = 24;
            baseline.Composer.Bounds.ChildBounds[0].fixedWidth = presentation.Size.Width;
            baseline.Composer.Bounds.ChildBounds[0].fixedHeight = presentation.Size.Height;
            baseline.Update(fixture.Native.Context);
            resized.Composer.Bounds.ChildBounds[0].fixedWidth = presentation.Size.Width;
            resized.Composer.Bounds.ChildBounds[0].fixedHeight = presentation.Size.Height;
            resized.Update(fixture.Native.Context);
            AssertScaled(baseline, resized, 1);
        }
    }

    public static IEnumerable<object[]> LayeredSizes()
    {
        foreach (string kind in new[] { "wetness", "body", "weather" })
        foreach (HudIndicatorStyle style in Enum.GetValues<HudIndicatorStyle>())
        foreach (int size in new[] { 6, 24, 48, 300 })
            yield return new object[] { kind, style, size };
    }

    [Theory]
    [MemberData(nameof(LayeredSizes))]
    public void LayeredSpritesMarkersAndFillBoundsScaleTogether(string kind, HudIndicatorStyle style, int size)
    {
        using var fixture = new LayeredIndicatorsFixture(2);
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        {
            var setting = new HudOverlaySizeSetting(kind == "weather" ? 36 : 24, () => size, _ => { }, () => { });
            using var baseline = new IndicatorComposition(fixture.Context.Api, overlay.CreateView(fixture.Context.Api, NativeViewRole.Live),
                fixture.Context, presentation.Size);
            using var resized = new IndicatorComposition(fixture.Context.Api,
                new SizedNativeView(overlay.CreateView(fixture.Context.Api, NativeViewRole.Live), setting), fixture.Context, setting.Apply(presentation.Size));
            AssertScaled(baseline, resized, setting.Scale);
            change(); prepare(); baseline.Update(fixture.Context); resized.Update(fixture.Context);
            AssertScaled(baseline, resized, setting.Scale);
            if (style == HudIndicatorStyle.Icon)
            {
                resized.View.CollectBackdrops(fixture.Context.Api.Render);
                Assert.Equal(2 * size / 36d, Assert.IsType<double>(Assert.Single(fixture.Resources.Backdrop.Invocations).Arguments[3]), 5);
                if (kind == "body")
                {
                    var originalFill = Assert.IsType<TextureElement>(baseline.Composer.GetElement("test:fill"));
                    var scaledFill = Assert.IsType<TextureElement>(resized.Composer.GetElement("test:fill"));
                    var originalClip = Reveal(originalFill, fixture.Context.Api.Render);
                    var scaledClip = Reveal(scaledFill, fixture.Context.Api.Render);
                    AssertClose((originalClip.Top - originalFill.State.Target.Top) * setting.Scale,
                        scaledClip.Top - scaledFill.State.Target.Top);
                    AssertClose(originalClip.Height * setting.Scale, scaledClip.Height);
                }
            }
        }
    }

    [Theory]
    [InlineData(6)] [InlineData(24)] [InlineData(48)] [InlineData(300)]
    public void TextRasterizationUsesTheIndicatorScaleIndependentlyOfNativeGuiScale(int size)
    {
        using var fixture = new HudNativeComponentFixture(2);
        var setting = new HudOverlaySizeSetting(24, () => size, _ => { }, () => { });
        int creates = 0;
        var view = new TextView(fixture.Api.Object, () => new("NAME", "READING", new(28, 14), new(49, 14)),
            create: (context, _, font) =>
            {
                creates++;
                Assert.Equal(2 * setting.Scale, context.GuiScale, 5);
                Assert.Equal(14 * setting.Scale, font.UnscaledFontsize, 5);
                Assert.Equal(setting.Scale, font.StrokeWidth, 5);
                return new(context.Api, 100 + creates, 20, 10);
            });
        using var composition = new IndicatorComposition(fixture.Api.Object, new SizedNativeView(view, setting), fixture.Context,
            setting.Apply(new(49, 28)));
        Assert.Equal(2, creates);
        composition.Update(fixture.Context);
        Assert.Equal(2, creates);
    }

    public static IEnumerable<object[]> InstrumentSizes()
    {
        foreach (string kind in new[] { "time", "temperature", "date" })
        foreach (EnvironmentalGaugeStyle style in Enum.GetValues<EnvironmentalGaugeStyle>())
        foreach (int size in new[] { 6, 96, 180, 300 })
            yield return new object[] { kind, style, size };
    }

    [Theory]
    [MemberData(nameof(InstrumentSizes))]
    public void InstrumentCanvasesMechanismsAndAllStylesScaleTogether(string kind, EnvironmentalGaugeStyle style, int size)
    {
        using var fixture = new InstrumentIndicatorsFixture(1.25f);
        var (overlay, presentation, prepare, change) = fixture.Create(kind, style);
        using (overlay)
        {
            var setting = new HudOverlaySizeSetting(kind == "date" ? 96 : 90, () => size, _ => { }, () => { });
            using var baseline = fixture.Mount(overlay, presentation);
            using var resized = new IndicatorComposition(fixture.Context.Api,
                new SizedNativeView(overlay.CreateView(fixture.Context.Api, NativeViewRole.SourceTrace), setting), fixture.Context, setting.Apply(presentation.Size));
            AssertScaled(baseline, resized, setting.Scale);
            change(); prepare(); baseline.Update(fixture.Context); resized.Update(fixture.Context);
            AssertScaled(baseline, resized, setting.Scale);
        }
    }

    [Fact]
    public void SizeInputAllowsTypingAndCommitsOnlyOnEnterOrSteps()
    {
        using var fixture = new HudNativeComponentFixture();
        var commits = new List<int>();
        using var input = new VanillaExpanded.HudOverlays.Interaction.HudOverlaySizeInput(fixture.Api.Object,
            ElementBounds.Fixed(0, 0, 100, 30).WithEmptyParent(), CairoFont.WhiteSmallText(), 24, commits.Add);
        Assert.Equal(2, input.Interval);
        input.SetValue("1"); input.SetValue("108");
        Assert.Empty(commits);
        input.OnKeyDown(fixture.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(new[] { 108 }, commits);
        input.OnKeyDown(fixture.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Up });
        Assert.Equal(110, commits.Last());
        input.OnKeyDown(fixture.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Down });
        Assert.Equal(108, commits.Last());
        input.SetValue("999"); input.OnFocusLost();
        Assert.Equal(300, commits.Last());
        input.SetValue(""); input.OnFocusLost();
        Assert.Equal(6, commits.Last());
    }

    private static void AssertScaled(IndicatorComposition baseline, IndicatorComposition resized, double scale)
    {
        var originalRoot = baseline.Composer.Bounds.ChildBounds[0];
        var scaledRoot = resized.Composer.Bounds.ChildBounds[0];
        var original = Descendants(originalRoot).ToArray();
        var scaled = Descendants(scaledRoot).ToArray();
        Assert.Equal(original.Length, scaled.Length);
        for (int index = 0; index < original.Length; index++)
        {
            AssertClose(original[index].OuterWidth * scale, scaled[index].OuterWidth);
            AssertClose(original[index].OuterHeight * scale, scaled[index].OuterHeight);
            AssertClose((original[index].renderX - originalRoot.renderX) * scale, scaled[index].renderX - scaledRoot.renderX);
            AssertClose((original[index].renderY - originalRoot.renderY) * scale, scaled[index].renderY - scaledRoot.renderY);
        }
    }

    private static IEnumerable<ElementBounds> Descendants(ElementBounds root)
    {
        foreach (var child in root.ChildBounds)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void AssertClose(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= 1,
        $"Expected {expected}, actual {actual}.");

    private static RectangleF Reveal(TextureElement fill, IRenderAPI renderer)
    {
        var mock = Mock.Get(renderer); mock.Invocations.Clear();
        fill.RenderInteractiveElements(0);
        var scissor = Assert.IsType<ElementBounds>(Assert.Single(mock.Invocations.Where(value => value.Method.Name == "PushScissor")).Arguments[0]);
        return new((float)scissor.renderX, (float)scissor.renderY, (float)scissor.OuterWidth, (float)scissor.OuterHeight);
    }
}
