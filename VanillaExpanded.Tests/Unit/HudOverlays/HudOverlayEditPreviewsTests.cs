using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks edit-only stand-ins without enabling gameplay features or drawing stale readings.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayEditPreviewsTests
{
    #region Public API
    /// <summary>Closing explicit editing removes hidden proxies even while the pointer stays free.</summary>
    [Fact]
    public void EditModeExitRetiresHiddenProxiesWithFreePointer()
    {
        using var fixture = new Fixture();
        fixture.Previews.Update(fixture.Context);
        Assert.Single(fixture.Previews.GetMembers());
        fixture.Gui.Object.OpenedGuis.Clear();
        Assert.False(fixture.Grabbed);
        fixture.Previews.Update(fixture.Context);
        Assert.Empty(fixture.Previews.GetMembers());
        Assert.False(fixture.Previews.IsDrawable(fixture.Registration));
        Assert.All(fixture.Textures, texture => Assert.True(texture.Disposed));
        fixture.VerifyNoFeatureCalls();
    }

    /// <summary>One failed stand-in does not hide healthy registrations and can recover on a later heartbeat.</summary>
    [Fact]
    public void FailedPlaceholderPreparationIsIsolatedAndRetryable()
    {
        using var fixture = new Fixture(); int attempts = 0, failures = 0;
        var other = new HudOverlayRegistration("mod:other", Mock.Of<IHudOverlay>(), () => false, fixture.Group.Id); fixture.Registry.Register(other);
        using var previews = new HudOverlayEditPreviews(fixture.Api.Object, fixture.Registry, _ => false, () => true, (_, _) => failures++,
            () => new WorkshopGaugeText((context, _) => {
                if (++attempts == 1) throw new InvalidOperationException();
                return new LoadedTexture(context.Api) { TextureId = 42, Width = 50, Height = 12, IgnoreUndisposed = true };
            }));
        previews.Update(fixture.Context); Assert.Single(previews.GetMembers()); Assert.Equal(1, failures);
        previews.Update(fixture.Context); Assert.Equal(2, previews.GetMembers().Count); Assert.Equal(1, failures);
        fixture.VerifyNoFeatureCalls();
    }

    /// <summary>Stable lettering is cached, invalidated by preparation changes, and retired when editing ends.</summary>
    [Fact]
    public void HiddenPlaceholderCachesPreparationAndReleasesOwnedResources()
    {
        using var fixture = new Fixture();
        fixture.Previews.Update(fixture.Context);
        Assert.Single(fixture.Previews.GetMembers()); Assert.Single(fixture.Textures);
        fixture.Previews.Update(fixture.Context); Assert.Single(fixture.Textures);
        fixture.Previews.Update(new HudOverlayPreparationContext(fixture.Api.Object, 2, "en"));
        Assert.Equal(2, fixture.Textures.Count); Assert.True(fixture.Textures[0].Disposed);
        fixture.Grabbed = true;
        Assert.False(fixture.Previews.IsDrawable(fixture.Registration)); Assert.Empty(fixture.Previews.GetMembers());
        fixture.Previews.Update(fixture.Context); Assert.True(fixture.Textures[1].Disposed);
        fixture.Grabbed = false; fixture.Previews.Update(fixture.Context); Assert.Equal(3, fixture.Textures.Count);
        fixture.Previews.Dispose(); Assert.True(fixture.Textures[2].Disposed);
        fixture.VerifyNoFeatureCalls();
    }

    /// <summary>Restored live content immediately supersedes placeholders and preserves the registration-owned reserved footprint.</summary>
    [Fact]
    public void LiveContentWinsAndRemovedRegistrationsCannotRemainTargets()
    {
        using var fixture = new Fixture(); fixture.Previews.Update(fixture.Context);
        Assert.Equal(fixture.Registration.ReservedSize, Assert.Single(fixture.Previews.GetMembers()).Value);
        fixture.Live = true; Assert.False(fixture.Previews.IsDrawable(fixture.Registration));
        fixture.Previews.Update(fixture.Context); Assert.True(fixture.Textures.Single().Disposed);
        fixture.Live = false; fixture.Previews.Update(fixture.Context);
        fixture.Handle.Dispose(); Assert.Empty(fixture.Previews.GetMembers());
        fixture.Previews.Update(fixture.Context); Assert.All(fixture.Textures, texture => Assert.True(texture.Disposed));
    }

    /// <summary>Normal host rendering dispatches edit stand-ins separately from feature foreground and shared backdrop providers.</summary>
    [Fact]
    public void HostDrawsPlaceholderInsteadOfHiddenFeatureAndBackdrop()
    {
        using var fixture = new Fixture(); fixture.Previews.Update(fixture.Context);
        var gui = fixture.Gui;
        gui.SetupGet(value => value.LoadedGuis).Returns(new List<GuiDialog>());
        gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { fixture.Api.Object, bounds, name }, null)!);
        var anchors = new HudOverlayAnchorContext(new Window(), () => 1, () => null); anchors.BeginFrame();
        using var layout = new HudOverlayGroupLayout(); layout.Apply(anchors, fixture.Group, fixture.Previews.GetMembers());
        var batch = new Mock<IHudOverlayBackdropBatch>();
        batch.Setup(value => value.Collect(It.IsAny<RectangleF>(), It.IsAny<Action>())).Callback<RectangleF, Action>((_, collect) => collect());
        using var host = new HudOverlayHost(fixture.Api.Object, fixture.Registry,
            registration => fixture.Live || fixture.Previews.IsDrawable(registration), (_, failure) => throw failure, backdropBatch: batch.Object, editPreviews: fixture.Previews);
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [fixture.Group.Id] = layout });
        host.TryOpen(false); HudOverlayNativeRenderPass.Render(host, .1f);
        Assert.Contains(fixture.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
        fixture.VerifyNoFeatureCalls();
        fixture.Live = true; fixture.Render.Invocations.Clear(); HudOverlayNativeRenderPass.Render(host, .1f);
        fixture.Overlay.Verify(value => value.Draw(fixture.Render.Object, It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), .1f), Times.Once);
        fixture.Backdrop.Verify(value => value.CollectBackdrops(fixture.Render.Object, It.IsAny<ElementBounds>()), Times.Once);
    }

    /// <summary>Native proxy resources belong to the composer without invoking hidden native factories.</summary>
    [Fact]
    public void NativeProxyCachesIndependentLetteringWithoutCreatingFeatureView()
    {
        using var fixture = new Fixture(native: true); fixture.Previews.Update(fixture.Context);
        fixture.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { fixture.Api.Object, bounds, name }, null)!);
        var anchors = new HudOverlayAnchorContext(new Window(), () => 1, () => null); anchors.BeginFrame();
        using var layout = new HudOverlayGroupLayout(); layout.Apply(anchors, fixture.Group, fixture.Previews.GetMembers());
        using var host = new HudOverlayHost(fixture.Api.Object, fixture.Registry, fixture.Previews.IsDrawable,
            (_, error) => throw error, editPreviews: fixture.Previews);
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [fixture.Group.Id] = layout });
        host.TryOpen(false); HudOverlayNativeRenderPass.Render(host, .1f);
        int allocations = fixture.Textures.Count;
        HudOverlayNativeRenderPass.Render(host, .1f);
        Assert.Equal(allocations, fixture.Textures.Count);
        Assert.IsType<VanillaExpanded.HudOverlays.Rendering.Native.EditProxyElement>(
            host.Composers[fixture.Group.Id].GetElement(fixture.Registration.Id + ":proxy"));
        fixture.Overlay.As<INativePresentation>().Verify(native => native.CreateView(It.IsAny<ICoreClientAPI>(),
            It.IsAny<NativeViewRole>()), Times.Never);
        fixture.VerifyNoFeatureCalls();
        host.Dispose();
        Assert.False(fixture.Textures.First().Disposed);
        Assert.All(fixture.Textures.Skip(1), texture => Assert.True(texture.Disposed));
    }
    #endregion

    #region Private
    /// <summary>Owns borrowed engine mocks and observes only stand-in texture lifetime.</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly Mock<ICoreClientAPI> Api = new();
        public readonly Mock<IGuiAPI> Gui = new();
        public readonly Mock<IRenderAPI> Render = new();
        public readonly Mock<IInputAPI> Input = new();
        public readonly Mock<IHudOverlay> Overlay = new();
        public readonly Mock<IHudOverlayBackdropProvider> Backdrop;
        public readonly HudOverlayRegistry Registry = new();
        public readonly HudOverlayGroup Group = new("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        public readonly HudOverlayRegistration Registration;
        public readonly IDisposable Handle;
        public readonly HudOverlayEditPreviews Previews;
        public readonly HudOverlayPreparationContext Context;
        public readonly List<LoadedTexture> Textures = new();
        private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
        private readonly float oldScale = RuntimeEnv.GUIScale;
        public bool Grabbed, Live;
        #region Public API
        /// <summary>Creates deterministic text dimensions and actual native layout without graphics allocation.</summary>
        public Fixture(bool native = false)
        {
            RuntimeEnv.GUIScale = 1;
            var language = new Mock<ITranslationService>();
            language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
            Lang.AvailableLanguages["en"] = language.Object;
            Api.SetupGet(value => value.Gui).Returns(Gui.Object); Api.SetupGet(value => value.Render).Returns(Render.Object);
            Render.SetupGet(value => value.FrameWidth).Returns(800); Render.SetupGet(value => value.FrameHeight).Returns(600);
            Gui.SetupGet(value => value.WindowBounds).Returns(new Window());
            Api.SetupGet(value => value.Input).Returns(Input.Object); Input.SetupGet(value => value.MouseGrabbed).Returns(() => Grabbed);
            HudOverlayEditFixture.Open(Api, Gui);
            if (native) Overlay.As<INativePresentation>();
            Backdrop = Overlay.As<IHudOverlayBackdropProvider>();
            Registry.RegisterGroup(Group); Registration = new HudOverlayRegistration("mod:hidden", Overlay.Object, () => false, Group.Id, reservedSize: new SizeF(74, 31));
            Handle = Registry.Register(Registration); Registry.BeginSession(Api.Object);
            Context = new HudOverlayPreparationContext(Api.Object, 1, "en");
            Previews = new HudOverlayEditPreviews(Api.Object, Registry, _ => Live, () => true, (_, failure) => throw failure,
                () => new WorkshopGaugeText((context, _) => { var texture = new LoadedTexture(context.Api) { TextureId = 42, Width = 64, Height = 12, IgnoreUndisposed = true }; Textures.Add(texture); return texture; }));
        }
        /// <summary>Asserts stand-ins never invoke feature sampling, sizing, preparation, foreground or backdrop methods.</summary>
        public void VerifyNoFeatureCalls()
        {
            Overlay.Verify(value => value.Refresh(), Times.Never); Overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
            Overlay.Verify(value => value.Measure(), Times.Never); Overlay.Verify(value => value.IsApplicable(), Times.Never);
            Overlay.Verify(value => value.Draw(It.IsAny<IRenderAPI>(), It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), It.IsAny<float>()), Times.Never);
            Backdrop.Verify(value => value.CollectBackdrops(It.IsAny<IRenderAPI>(), It.IsAny<ElementBounds>()), Times.Never);
        }
        /// <summary>Retires preview-owned resources before their registration metadata.</summary>
        public void Dispose() { try { Previews.Dispose(); Registry.Dispose(); } finally { RuntimeEnv.GUIScale = oldScale; if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english; } }
        #endregion
    }

    /// <summary>Supplies a native parent with deterministic viewport geometry.</summary>
    private sealed class Window : ElementBounds
    {
        #region Public API
        /// <summary>Marks this parent as the native viewport boundary.</summary>
        public Window() { IsWindowBounds = true; CalcWorldBounds(); }
        /// <summary>Returns a parentless origin.</summary>
        public override double renderX => 0;
        /// <summary>Returns a parentless origin.</summary>
        public override double renderY => 0;
        /// <summary>Returns a parentless native hit-test origin.</summary>
        public override double absX => 0;
        /// <summary>Returns a parentless native hit-test origin.</summary>
        public override double absY => 0;
        /// <summary>Calculates synthetic viewport bounds.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 800; absInnerHeight = 600; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}
