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
        Assert.Empty(fixture.Textures);
        fixture.VerifyNoFeatureCalls();
    }

    /// <summary>One failed native glyph preparation is isolated and can recover without sampling the hidden feature.</summary>
    [Fact]
    public void FailedPlaceholderPreparationIsIsolatedAndRetryable()
    {
        using var fixture = new Fixture(); fixture.FailGlyph = true;
        fixture.Previews.Update(fixture.Context);
        var view = new VanillaExpanded.HudOverlays.Rendering.Native.EditProxyView(fixture.Api.Object, fixture.Previews, fixture.Registration, _ => { });
        using (view)
        {
            Assert.Throws<InvalidOperationException>(() => view.Prepare(fixture.Context));
            fixture.FailGlyph = false;
            using var composition = new IndicatorComposition(fixture.Api.Object, view, fixture.Context, fixture.Registration.ReservedSize!.Value);
            composition.Composer.Render(0);
            Assert.Single(fixture.Textures);
        }
        Assert.True(fixture.Textures.Single().Disposed); fixture.VerifyNoFeatureCalls();
    }


    /// <summary>Membership prepares no glyphs; the native element caches them and owns cleanup independently of metadata.</summary>
    [Fact]
    public void HiddenPlaceholderCachesPreparationAndReleasesOwnedResources()
    {
        using var fixture = new Fixture(); fixture.Previews.Update(fixture.Context);
        Assert.Single(fixture.Previews.GetMembers()); Assert.Empty(fixture.Textures);
        using var view = new VanillaExpanded.HudOverlays.Rendering.Native.EditProxyView(fixture.Api.Object, fixture.Previews, fixture.Registration, _ => { });
        using var composition = new IndicatorComposition(fixture.Api.Object, view, fixture.Context, fixture.Registration.ReservedSize!.Value);
        Assert.Single(fixture.Textures);
        fixture.Previews.Update(fixture.Context); composition.View.Prepare(fixture.Context); Assert.Single(fixture.Textures);
        composition.View.Prepare(new(fixture.Api.Object, 2, "en"));
        Assert.Equal(2, fixture.Textures.Count); Assert.True(fixture.Textures[0].Disposed);
        fixture.Grabbed = true; Assert.False(fixture.Previews.IsDrawable(fixture.Registration));
        fixture.Previews.Update(fixture.Context); Assert.Empty(fixture.Previews.GetMembers());
        Assert.False(fixture.Textures[1].Disposed);
        composition.Dispose(); Assert.True(fixture.Textures[1].Disposed);
        fixture.VerifyNoFeatureCalls();
    }

    /// <summary>A view locale change localizes proxy lettering before the next metadata heartbeat without feature calls.</summary>
    [Fact]
    public void NativeProxyLocaleRefreshDoesNotWaitForMetadataHeartbeat()
    {
        using var fixture = new Fixture();
        var previous = Lang.AvailableLanguages.GetValueOrDefault("de");
        var german = new Mock<ITranslationService>();
        german.Setup(value => value.HasTranslation(It.IsAny<string>(), false)).Returns(true);
        german.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("Versteckter Gegenstand");
        Lang.AvailableLanguages["de"] = german.Object;
        try
        {
            var registration = new HudOverlayRegistration("mod:localized", Mock.Of<IHudOverlay>(), () => false,
                fixture.Group.Id, hoverLabelKey: "mod:hidden-label");
            using var handle = fixture.Registry.Register(registration);
            fixture.Previews.Update(fixture.Context);
            using var view = new VanillaExpanded.HudOverlays.Rendering.Native.EditProxyView(fixture.Api.Object,
                fixture.Previews, registration, _ => { });
            using var composition = new IndicatorComposition(fixture.Api.Object, view, fixture.Context, new(74, 31));
            string initial = fixture.Values.Single();
            view.Prepare(new(fixture.Api.Object, 1, "de"));
            Assert.Equal("VG", fixture.Values.Last()); Assert.NotEqual(initial, fixture.Values.Last());
            Assert.True(fixture.Textures[0].Disposed);
            Assert.False(fixture.Textures[1].Disposed);
            fixture.VerifyNoFeatureCalls();
        }
        finally
        {
            if (previous == null) Lang.AvailableLanguages.Remove("de");
            else Lang.AvailableLanguages["de"] = previous;
        }
    }


    /// <summary>Restored live content immediately supersedes placeholders and preserves the registration-owned reserved footprint.</summary>
    [Fact]
    public void LiveContentWinsAndRemovedRegistrationsCannotRemainTargets()
    {
        using var fixture = new Fixture(); fixture.Previews.Update(fixture.Context);
        Assert.Equal(fixture.Registration.ReservedSize, Assert.Single(fixture.Previews.GetMembers()).Value);
        fixture.Live = true; Assert.False(fixture.Previews.IsDrawable(fixture.Registration));
        fixture.Previews.Update(fixture.Context); Assert.Empty(fixture.Textures);
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
        fixture.Live = true; fixture.Render.Invocations.Clear();
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [fixture.Group.Id] = layout });
        HudOverlayNativeRenderPass.Render(host, .1f);
        fixture.Overlay.Verify(value => value.CreateView(fixture.Api.Object, NativeViewRole.Live), Times.Once);
        Assert.Equal(1, fixture.LiveDraws); Assert.Equal(1, fixture.LiveBackdrops);
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
        Assert.IsType<VanillaExpanded.HudOverlays.Rendering.Native.TextElement>(
            host.Composers[fixture.Group.Id].GetElement(fixture.Registration.Id + ":proxy"));
        fixture.Overlay.As<INativePresentation>().Verify(native => native.CreateView(It.IsAny<ICoreClientAPI>(),
            It.IsAny<NativeViewRole>()), Times.Never);
        fixture.VerifyNoFeatureCalls();
        host.Dispose();
        Assert.Single(fixture.Textures); Assert.All(fixture.Textures, texture => Assert.True(texture.Disposed));
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
        public readonly HudOverlayRegistry Registry = new();
        public readonly HudOverlayGroup Group = new("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        public readonly HudOverlayRegistration Registration;
        public readonly IDisposable Handle;
        public readonly HudOverlayEditPreviews Previews;
        public readonly HudOverlayPreparationContext Context;
        public readonly List<LoadedTexture> Textures = new();
        public readonly List<string> Values = new();
        private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
        private readonly float oldScale = RuntimeEnv.GUIScale;
        public bool Grabbed, Live, FailGlyph;
        public int LiveDraws, LiveBackdrops;
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
            Overlay.Setup(value => value.CreateView(Api.Object, It.IsAny<NativeViewRole>())).Returns(() => new CallbackView(Api.Object, (_, _) => LiveDraws++, () => LiveBackdrops++));
            Registry.RegisterGroup(Group); Registration = new HudOverlayRegistration("mod:hidden", Overlay.Object, () => false, Group.Id, reservedSize: new SizeF(74, 31));
            Handle = Registry.Register(Registration); Registry.BeginSession(Api.Object);
            Context = new HudOverlayPreparationContext(Api.Object, 1, "en");
            Previews = new HudOverlayEditPreviews(Api.Object, Registry, _ => Live, () => true, (_, failure) => throw failure,
                (context, value, _) => { if (FailGlyph) throw new InvalidOperationException("glyph"); Values.Add(value); var texture = new LoadedTexture(context.Api) { TextureId = 42, Width = 64, Height = 12, IgnoreUndisposed = true }; Textures.Add(texture); return texture; });
        }
        /// <summary>Asserts stand-ins never invoke feature sampling, sizing, preparation, foreground or backdrop methods.</summary>
        public void VerifyNoFeatureCalls()
        {
            Overlay.Verify(value => value.Refresh(), Times.Never); Overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
            Overlay.Verify(value => value.Measure(), Times.Never); Overlay.Verify(value => value.IsApplicable(), Times.Never);
            Overlay.Verify(value => value.CreateView(It.IsAny<ICoreClientAPI>(), It.IsAny<NativeViewRole>()), Times.Never);
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
