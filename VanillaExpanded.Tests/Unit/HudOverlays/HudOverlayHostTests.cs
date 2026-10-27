using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Updating;
using Vintagestory.API.Client;
namespace VanillaExpanded.Tests.Unit.HudOverlays;
/// <summary>Exercises native passive lifecycle and render state restoration without a graphics context.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayHostTests : IDisposable
{
    private readonly float oldScale = Vintagestory.API.Config.RuntimeEnv.GUIScale;
    #region Public API
    /// <summary>Provides a deterministic native GUI scale for headless composition.</summary>
    public HudOverlayHostTests() => Vintagestory.API.Config.RuntimeEnv.GUIScale = 1;
    /// <summary>Restores the borrowed native GUI global after each check.</summary>
    public void Dispose() => Vintagestory.API.Config.RuntimeEnv.GUIScale = oldScale;
    #region Native lifecycle and drawing
    /// <summary>Collects visible members once before foreground rendering while failures and removals preserve the frame snapshot.</summary>
    [Fact]
    public void SharedBackdropPassPrecedesForegroundAndIsolatesProviderFailure()
    {
        var api = new Mock<ICoreClientAPI>(); var gui = new Mock<IGuiAPI>(); var renderer = new Mock<IRenderAPI>();
        api.SetupGet(x => x.Gui).Returns(gui.Object); api.SetupGet(x => x.Render).Returns(renderer.Object);
        api.SetupGet(x => x.Input).Returns(new Mock<IInputAPI>().Object);
        gui.SetupGet(x => x.LoadedGuis).Returns(new List<GuiDialog>());
        gui.Setup(x => x.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>())).Returns((string name, ElementBounds bounds) =>
            (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { api.Object, bounds, name }, null)!);
        using var registry = new HudOverlayRegistry();
        var group = new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        registry.RegisterGroup(group);
        var calls = new List<string>(); var sizes = new List<KeyValuePair<HudOverlayRegistration, SizeF>>();
        var handles = new List<IDisposable>();
        foreach (string name in new[] { "a", "b", "hidden", "empty" })
        {
            var overlay = new Mock<IHudOverlay>();
        overlay.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(api.Object));
            var registration = new HudOverlayRegistration("mod:" + name, overlay.Object, () => true, group.Id);
            handles.Add(registry.Register(registration));
            sizes.Add(new(registration, name == "empty" ? SizeF.Empty : new SizeF(20, 10)));
            overlay.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(api.Object,
                (_, _) => calls.Add("draw:" + name), () =>
            {
                calls.Add("collect:" + name);
                if (name == "a") { handles[0].Dispose(); overlay.Verify(x => x.Dispose(), Times.Never); throw new InvalidOperationException(); }
            }));
        }
        registry.BeginSession(api.Object);
        var anchors = new HudOverlayAnchorContext(new Window(), () => 1, () => null); anchors.BeginFrame();
        using var layout = new HudOverlayGroupLayout(); layout.Apply(anchors, group, sizes);
        var batch = new Mock<IHudOverlayBackdropBatch>();
        batch.Setup(x => x.Collect(It.IsAny<RectangleF>(), It.IsAny<Action>())).Callback<RectangleF, Action>((_, collect) => collect());
        batch.Setup(x => x.Flush(renderer.Object)).Callback(() => calls.Add("flush"));
        int failures = 0;
        using var host = new HudOverlayHost(api.Object, registry, r => r.Id != "mod:hidden", (_, _) => failures++, backdropBatch: batch.Object);
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [group.Id] = layout });
        host.TryOpen(false); HudOverlayNativeRenderPass.Render(host, .1f);
        Assert.Equal(1, failures); Assert.Equal(1, calls.Count(x => x == "flush"));
        Assert.DoesNotContain("collect:hidden", calls); Assert.DoesNotContain("collect:empty", calls);
        Assert.All(calls.Where(x => x.StartsWith("collect:")), x => Assert.True(calls.IndexOf(x) < calls.IndexOf("flush")));
        Assert.All(calls.Where(x => x.StartsWith("draw:")), x => Assert.True(calls.IndexOf(x) > calls.IndexOf("flush")));
        Assert.Contains("collect:b", calls); Assert.Contains("draw:b", calls);
        batch.Verify(x => x.Clear(), Times.Exactly(2));
        // Removal retires the generation before releasing its controller. Rebuild surviving members
        // before exercising the separate composer enablement and unavailable-anchor gates.
        registry.RunPass(_ => { });
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [group.Id] = layout });
        calls.Clear(); host.Composers[group.Id].Enabled = false; HudOverlayNativeRenderPass.Render(host, .1f);
        Assert.Equal(new[] { "flush" }, calls);
        calls.Clear(); host.Composers[group.Id].Enabled = true;
        typeof(HudOverlayGroupLayout).GetProperty(nameof(HudOverlayGroupLayout.Available))!.SetValue(layout, false);
        HudOverlayNativeRenderPass.Render(host, .1f);
        Assert.Equal(new[] { "flush" }, calls);
        registry.RunPass(_ => { });
    }
    /// <summary>Runs native composition after scheduling a zero-size fair-weather member ahead of the time icon.</summary>
    [Fact]
    public void FairWeatherSiblingDoesNotSuppressScheduledTimeIcon()
    {
        var api = new Mock<ICoreClientAPI>(); var gui = new Mock<IGuiAPI>(); var renderer = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object); api.SetupGet(value => value.Render).Returns(renderer.Object);
        api.SetupGet(value => value.Input).Returns(new Mock<IInputAPI>().Object);
        gui.SetupGet(value => value.LoadedGuis).Returns(new List<GuiDialog>());
        gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>())).Returns((string name, ElementBounds bounds) =>
            (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { api.Object, bounds, name }, null)!);
        using var registry = new HudOverlayRegistry();
        var group = new HudOverlayGroup("weather-status", new HudOverlayPlacement("hotbar", HudOverlayPoint.RightBottom,
            HudOverlayPoint.LeftBottom, 4, 4), new HudOverlayPacking(HudOverlayDirection.Horizontal));
        registry.RegisterGroup(group);
        var weather = new Mock<IHudOverlay>(); var time = new Mock<IHudOverlay>();
        weather.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(api.Object));
        using var icon = new ApproximateTimePresentation(
            load: (context, _) => new(new LoadedTexture(context.Api) { TextureId = 1, IgnoreUndisposed = true }, false),
            createFace: (context, _) => new LoadedTexture(context.Api) { TextureId = 20 });
        time.As<INativePresentation>().Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>()))
            .Returns(() => icon.CreateView(api.Object));
        icon.SetContent(ApproximateTimePeriod.Unknown, .5);
        time.Setup(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>())).Callback<HudOverlayPreparationContext>(icon.Prepare);
        weather.Setup(value => value.IsApplicable()).Returns(true); time.Setup(value => value.IsApplicable()).Returns(true);
        SizeF weatherSize = SizeF.Empty;
        weather.Setup(value => value.Measure()).Returns(() => weatherSize); time.Setup(value => value.Measure()).Returns(() => icon.Size);
        weather.Setup(value => value.Refresh()).Returns(HudOverlayChange.Measurement);
        registry.Register(new HudOverlayRegistration("mod:weather", weather.Object, () => true, group.Id));
        registry.Register(new HudOverlayRegistration("mod:time", time.Object, () => true, group.Id, order: 100));
        registry.BeginSession(api.Object);
        var anchors = new HudOverlayAnchorContext(new Window(200), () => 1, () => new RectangleF(50, 50, 100, 20));
        anchors.BeginFrame();
        long now = 0;
        var scheduler = new HudOverlayScheduler(registry, () => now);
        scheduler.Update(anchors, new HudOverlayPreparationContext(api.Object, 1, "en"), true);
        using var layout = new HudOverlayGroupLayout(); layout.Apply(anchors, group, scheduler.GetMembers(group.Id));
        Assert.Equal(2, layout.Members.Count);
        var emptyWeather = layout.Members.Single(value => value.Registration.Id == "mod:weather");
        Assert.Equal(0, emptyWeather.Bounds.OuterWidth); Assert.Equal(0, emptyWeather.Bounds.OuterHeight);
        var member = layout.Members.Single(value => value.Registration.Id == "mod:time");
        using var host = new HudOverlayHost(api.Object, registry, scheduler.IsDrawable, (_, error) => throw error);
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [group.Id] = layout });
        host.TryOpen(false); HudOverlayNativeRenderPass.Render(host, .1f);
        renderer.Verify(value => value.Render2DTexturePremultipliedAlpha(20, It.IsAny<float>(), It.IsAny<float>(),
            It.IsAny<float>(), It.IsAny<float>(), 51, null), Times.Once);
        // Entering and leaving drawable membership replaces the native generation; the time view remains visible.
        var composer = host.Composers[group.Id];
        foreach (SizeF nextSize in new[] { new SizeF(36, 36), SizeF.Empty })
        {
            weatherSize = nextSize; now += 1000; anchors.BeginFrame();
            scheduler.Update(anchors, new HudOverlayPreparationContext(api.Object, 1, "en"), true);
            layout.Apply(anchors, group, scheduler.GetMembers(group.Id));
            host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [group.Id] = layout });
            Assert.NotSame(composer, host.Composers[group.Id]);
            Assert.True(layout.Members.Single(value => value.Registration.Id == "mod:time").Clip.Width > 0);
            HudOverlayNativeRenderPass.Render(host, .1f);
        }
        renderer.Verify(value => value.Render2DTexturePremultipliedAlpha(20, It.IsAny<float>(), It.IsAny<float>(),
            It.IsAny<float>(), It.IsAny<float>(), 51, null), Times.Exactly(3));
    }
    /// <summary>Native open/close registration remains focus-free and final cleanup is idempotent.</summary>
    [Fact]
    public void NativeLifecycleDeclinesInputAndClosesBeforeDisposal()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var loaded = new List<GuiDialog>();
        api.SetupGet(x => x.Gui).Returns(gui.Object);
        gui.SetupGet(x => x.LoadedGuis).Returns(loaded);
        gui.Setup(x => x.RegisterDialog(It.IsAny<GuiDialog[]>())).Callback<GuiDialog[]>(dialogs => loaded.AddRange(dialogs));
        gui.Setup(x => x.TriggerDialogClosed(It.IsAny<GuiDialog>())).Callback<GuiDialog>(dialog => loaded.Remove(dialog));
        using var registry = new HudOverlayRegistry();
        var host = new HudOverlayHost(api.Object, registry, _ => true, (_, _) => { });
        Assert.False(host.Focusable);
        Assert.False(host.ShouldReceiveKeyboardEvents());
        Assert.False(host.ShouldReceiveMouseEvents());
        Assert.False(host.CaptureAllInputs());
        Assert.False(host.CaptureRawMouse());
        Assert.False(host.OnEscapePressed());
        Assert.False(host.PrefersUngrabbedMouse);

        Assert.True(host.UnregisterOnClose);
        var artwork = (GuiDialog)typeof(HudOverlayHost).GetField("artworkDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Assert.False(artwork.Focusable);
        Assert.True(artwork.DrawOrder < host.DrawOrder);
        Assert.False(artwork.ShouldReceiveKeyboardEvents());
        Assert.False(artwork.ShouldReceiveMouseEvents());
        Assert.False(artwork.CaptureAllInputs());
        Assert.False(artwork.CaptureRawMouse());
        Assert.True(artwork.UnregisterOnClose);
        host.TryOpen(false);
        Assert.True(artwork.IsOpened());
        host.TryClose();
        Assert.False(artwork.IsOpened());
        host.TryOpen(false);
        host.Dispose();
        host.Dispose();
        Assert.Empty(loaded);
        Assert.False(artwork.IsOpened());
        int staleCallbacks = 0;
        host.BeforeRender = () => staleCallbacks++;
        artwork.OnRenderGUI(.1f);
        Assert.Equal(0, staleCallbacks);
        gui.Verify(x => x.RequestFocus(It.IsAny<GuiDialog>()), Times.Never);
        gui.Verify(x => x.TriggerDialogClosed(host), Times.Exactly(2));
        gui.Verify(x => x.TriggerDialogClosed(artwork), Times.Exactly(2));
    }
    /// <summary>Native component failures restore the supplied scissor and reach the feature diagnostic boundary.</summary>
    [Fact]
    public void ConsumerFailureRestoresNativeScissorAndDispatchesDiagnostic()
    {
        using var fixture = new HudNativeHostFixture();
        fixture.Native.Setup(value => value.CreateView(fixture.Components.Api.Object, It.IsAny<NativeViewRole>()))
            .Returns(() => new CallbackView(fixture.Components.Api.Object, (_, _) => throw new InvalidOperationException("draw")));
        fixture.Synchronize();
        fixture.Render(); Assert.Single(fixture.Failures);
        var calls = fixture.Components.Render.Invocations;
        Assert.Equal(calls.Count(call => call.Method.Name == "PushScissor"), calls.Count(call => call.Method.Name == "PopScissor"));
        Assert.Contains(calls, call => call.Method.Name == "PushScissor" && ((ElementBounds)call.Arguments[0]).OuterWidth == fixture.Layout.Members.Single().Clip.Width);
    }

    /// <summary>Uses installed native composer registration and composition, reusing it for position-only changes.</summary>
    [Fact]
    public void NativeComposerUsesDistinctChildAndReusesUnchangedResources()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        api.SetupGet(x => x.Gui).Returns(gui.Object);
        gui.SetupGet(x => x.LoadedGuis).Returns(new List<GuiDialog>());
        gui.Setup(x => x.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>())).Returns((string name, ElementBounds bounds) =>
            (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { api.Object, bounds, name }, null)!);
        using var registry = new HudOverlayRegistry();
        var overlay = new Mock<IHudOverlay>();
        overlay.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(api.Object));
        var registration = new HudOverlayRegistration("mod:a", overlay.Object, () => true, "group");
        var window = ElementBounds.Fixed(0, 0, 200, 100).WithEmptyParent();
        registry.RegisterGroup(new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
        registry.Register(registration);
        window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => 1, () => null);
        anchors.BeginFrame();
        using var layout = new HudOverlayGroupLayout();
        var group = new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        layout.Apply(anchors, group, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(40, 40)) });
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, _) => { });
        var layouts = new Dictionary<string, HudOverlayGroupLayout> { ["group"] = layout };
        host.Synchronize(layouts);
        var composer = host.Composers["group"];
        Assert.True(composer.Composed);
        Assert.Same(layout.Root, composer.Bounds);
        Assert.All(layout.Root.ChildBounds, child => Assert.NotSame(layout.Root, child));
        host.Synchronize(layouts);
        layout.Root.renderOffsetX += 5;
        layout.Apply(anchors, group, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(40, 40)) });
        host.Synchronize(layouts);
        Assert.Same(composer, host.Composers["group"]);
        gui.Verify(x => x.CreateCompo(It.IsAny<string>(), layout.Root), Times.Once);
    }
    /// <summary>Installed composer dispatch preserves native HUD state, peer drawing and deferred ownership during errors.</summary>
    [Fact]
    public void NativeDrawPassDefersRemovalAndRestoresHudTransform()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var renderer = new Mock<IRenderAPI>();
        api.SetupGet(x => x.Gui).Returns(gui.Object);
        api.SetupGet(x => x.Render).Returns(renderer.Object);
        api.SetupGet(x => x.Input).Returns(new Mock<IInputAPI>().Object);
        gui.SetupGet(x => x.LoadedGuis).Returns(new List<GuiDialog>());
        gui.Setup(x => x.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>())).Returns((string name, ElementBounds bounds) =>
            (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { api.Object, bounds, name }, null)!);
        using var registry = new HudOverlayRegistry();
        var group = new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        registry.RegisterGroup(group);
        var a = new Mock<IHudOverlay>();
        var b = new Mock<IHudOverlay>();
        var ra = new HudOverlayRegistration("mod:a", a.Object, () => true, "group");
        var rb = new HudOverlayRegistration("mod:b", b.Object, () => true, "group");
        var handle = registry.Register(ra);
        registry.Register(rb);
        registry.BeginSession(api.Object);
        var anchors = new HudOverlayAnchorContext(new Window(), () => 1, () => null);
        anchors.BeginFrame();
        using var layout = new HudOverlayGroupLayout();
        layout.Apply(anchors, group, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(ra, new SizeF(20, 10)),
            new KeyValuePair<HudOverlayRegistration, SizeF>(rb, new SizeF(20, 10)) });
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, _) => { });
        a.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(api.Object, (_, _) =>
        {
            handle.Dispose();
            a.Verify(x => x.Dispose(), Times.Never);
            throw new InvalidOperationException();
        }));
        int bDraws = 0;
        b.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(api.Object, (_, _) => bDraws++));
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { ["group"] = layout });
        host.TryOpen(false);
        HudOverlayNativeRenderPass.Render(host, .1f);
        Assert.Equal(1, bDraws);
        a.Verify(x => x.Refresh(), Times.Never);
        a.Verify(x => x.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        renderer.Verify(x => x.GlTranslate(0, 0, It.Is<float>(depth => depth < 0)), Times.Once);
        renderer.Verify(x => x.GlPushMatrix(), Times.Exactly(2));
        renderer.Verify(x => x.GlPopMatrix(), Times.Exactly(2));
        renderer.Verify(x => x.PopScissor(), Times.Exactly(2));
        a.Verify(x => x.Dispose(), Times.Never);
        registry.RunPass(_ => { });
        a.Verify(x => x.EndSession(), Times.Once);
        a.Verify(x => x.Dispose(), Times.Once);
    }
    #endregion
    #endregion
    /// <summary>Supplies actual native window parent semantics without a graphics platform.</summary>
    private sealed class Window : ElementBounds
    {
        private readonly double height;
        #region Public API
        /// <summary>Creates deterministic native pixel dimensions.</summary>
        public Window(double height = 100) { this.height = height; IsWindowBounds = true; CalcWorldBounds(); }
        /// <summary>Returns the native framebuffer origin without a graphics platform.</summary>
        public override double renderX => 0;
        /// <summary>Returns native absolute hit-test origin without a platform parent.</summary>
        public override double absX => 0;
        /// <summary>Returns native absolute hit-test origin without a platform parent.</summary>
        public override double absY => 0;
        /// <summary>Returns the native framebuffer origin without a graphics platform.</summary>
        public override double renderY => 0;
        /// <summary>Provides framebuffer dimensions used by native child composition.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 200; absInnerHeight = height; Initialized = true; }
        #endregion
    }
}
