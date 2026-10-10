using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
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
        Assert.Equal(.1, host.DrawOrder);
        Assert.True(host.UnregisterOnClose);
        host.TryOpen(false);
        host.TryClose();
        host.TryOpen(false);
        host.Dispose();
        host.Dispose();
        Assert.Empty(loaded);
        gui.Verify(x => x.RequestFocus(It.IsAny<GuiDialog>()), Times.Never);
        gui.Verify(x => x.TriggerDialogClosed(host), Times.Exactly(2));
    }
    /// <summary>Assigned oversized clipping is supplied to the native scissor and restored on consumer failure.</summary>
    [Fact]
    public void ConsumerFailureRestoresNativeScissorAndDispatchesDiagnostic()
    {
        var api = new Mock<ICoreClientAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(x => x.Render).Returns(render.Object);
        using var registry = new HudOverlayRegistry();
        var overlay = new Mock<IHudOverlay>();
        var registration = new HudOverlayRegistration("mod:a", overlay.Object, () => true, "group");
        var window = new Window();
        window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => 1, () => null);
        anchors.BeginFrame();
        using var layout = new HudOverlayGroupLayout();
        layout.Apply(anchors, new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)),
            new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(400, 40)) });
        int failures = 0;
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, _) => failures++);
        ((HashSet<HudOverlayRegistration>)typeof(HudOverlayHost).GetField("active", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!).Add(registration);
        var member = layout.Members.Single();
        overlay.Setup(x => x.Draw(render.Object, member.Bounds, member.Clip, .1f)).Throws(new InvalidOperationException());
        typeof(HudOverlayHost).GetMethod("DrawGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host, new object[] { layout, .1f });
        Assert.Equal(1, failures);
        render.Verify(x => x.PushScissor(It.Is<ElementBounds>(bounds => bounds.renderX == member.Clip.X && bounds.renderY == member.Clip.Y
            && bounds.OuterWidth == member.Clip.Width && bounds.OuterHeight == member.Clip.Height), true), Times.Once);
        render.Verify(x => x.PopScissor(), Times.Once);
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
        var registration = new HudOverlayRegistration("mod:a", overlay.Object, () => true, "group");
        var window = ElementBounds.Fixed(0, 0, 200, 100).WithEmptyParent();
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
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { ["group"] = layout });
        a.Setup(x => x.Draw(renderer.Object, It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), .1f)).Callback(() =>
        {
            handle.Dispose();
            a.Verify(x => x.Dispose(), Times.Never);
        }).Throws(new InvalidOperationException());
        host.TryOpen(false);
        host.OnRenderGUI(.1f);
        b.Verify(x => x.Draw(renderer.Object, It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), .1f), Times.Once);
        a.Verify(x => x.Refresh(), Times.Never);
        a.Verify(x => x.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        renderer.Verify(x => x.GlTranslate(0, 0, -150), Times.Once);
        renderer.Verify(x => x.GlPushMatrix(), Times.Once);
        renderer.Verify(x => x.GlPopMatrix(), Times.Once);
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
        #region Public API
        /// <summary>Creates deterministic native pixel dimensions.</summary>
        public Window() { IsWindowBounds = true; CalcWorldBounds(); }
        /// <summary>Returns the native framebuffer origin without a graphics platform.</summary>
        public override double renderX => 0;
        /// <summary>Returns native absolute hit-test origin without a platform parent.</summary>
        public override double absX => 0;
        /// <summary>Returns native absolute hit-test origin without a platform parent.</summary>
        public override double absY => 0;
        /// <summary>Returns the native framebuffer origin without a graphics platform.</summary>
        public override double renderY => 0;
        /// <summary>Provides framebuffer dimensions used by native child composition.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 200; absInnerHeight = 100; Initialized = true; }
        #endregion
    }
}
