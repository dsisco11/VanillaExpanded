using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.WindSpeed;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises optional native selection and the external legacy lifetime boundary.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudNativeCompatibilityTests
{
    #region Public API
    #region Native capability
    /// <summary>Optional capability selects real native components without touching controller sampling or lifecycle.</summary>
    [Fact]
    public void OptionalCapabilityCreatesIndependentViewsWithoutSampling()
    {
        using var fixture = new HudNativeComponentFixture();
        var overlay = new Mock<IHudOverlay>();
        var capability = overlay.As<INativePresentation>();
        capability.Setup(native => native.CreateView(fixture.Api.Object, It.IsAny<NativeViewRole>()))
            .Returns(() => fixture.CreateView());
        using var live = ViewFactory.Create(overlay.Object, fixture.Api.Object, NativeViewRole.Live, fixture.Failures.Add);
        using var source = ViewFactory.Create(overlay.Object, fixture.Api.Object, NativeViewRole.SourceTrace, fixture.Failures.Add);
        Assert.IsType<IconReadingView>(live); Assert.NotSame(live, source);
        live.Prepare(fixture.Context); source.Prepare(fixture.Context);
        capability.Verify(native => native.CreateView(fixture.Api.Object, NativeViewRole.Live), Times.Once);
        capability.Verify(native => native.CreateView(fixture.Api.Object, NativeViewRole.SourceTrace), Times.Once);
        overlay.Verify(overlay => overlay.Refresh(), Times.Never);
        overlay.Verify(overlay => overlay.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        overlay.Verify(overlay => overlay.Measure(), Times.Never);
        overlay.Verify(overlay => overlay.BeginSession(It.IsAny<ICoreClientAPI>()), Times.Never);
        overlay.Verify(overlay => overlay.EndSession(), Times.Never);
        overlay.Verify(overlay => overlay.Dispose(), Times.Never);
        capability.Verify(native => native.BeginFrame(It.IsAny<float>()), Times.Never);
    }
    /// <summary>Missing built-in capability is an explicit error instead of an invisible compatibility fallback.</summary>
    [Fact]
    public void BuiltInCannotSilentlyUseExternalAdapter()
    {
        using var fixture = new HudNativeComponentFixture();
        using var overlay = new WindSpeedOverlay();
        // This check continues to apply after this particular built-in gains the optional capability.
        if ((IHudOverlay)overlay is INativePresentation)
        {
            using var view = ViewFactory.Create(overlay, fixture.Api.Object, NativeViewRole.Live, fixture.Failures.Add);
            Assert.IsNotType<LegacyView>(view);
        }
        else
            Assert.Throws<InvalidOperationException>(() => ViewFactory.Create(overlay, fixture.Api.Object,
                NativeViewRole.Live, fixture.Failures.Add));
    }
    /// <summary>A broken native provider is not silently retried through the old drawing contract.</summary>
    [Fact]
    public void FailedNativeFactoryNeverFallsBackToLegacy()
    {
        using var fixture = new HudNativeComponentFixture();
        var overlay = new Mock<IHudOverlay>();
        overlay.As<INativePresentation>()
            .Setup(native => native.CreateView(fixture.Api.Object, NativeViewRole.Live))
            .Throws(new InvalidOperationException("factory failure"));
        Assert.Throws<InvalidOperationException>(() => ViewFactory.Create(overlay.Object, fixture.Api.Object,
            NativeViewRole.Live, fixture.Failures.Add));
        overlay.Verify(overlay => overlay.Draw(It.IsAny<IRenderAPI>(), It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), It.IsAny<float>()), Times.Never);
        overlay.Verify(overlay => overlay.Dispose(), Times.Never);
    }
    #endregion
    #region Legacy compatibility
    /// <summary>The external adapter preserves supplied clipping, elapsed time and settled opacity.</summary>
    [Theory]
    [InlineData(NativeViewRole.Live)]
    [InlineData(NativeViewRole.SourceTrace)]
    public void LegacyCallbackUsesPreparedDrawAndBackdropState(NativeViewRole role)
    {
        using var fixture = new HudNativeComponentFixture();
        var overlay = new Mock<IHudOverlay>();
        var backdrop = overlay.As<IHudOverlayBackdropProvider>();
        const float opacity = .6f;
        var observed = new List<float>();
        overlay.Setup(overlay => overlay.Draw(fixture.Render.Object, It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), It.IsAny<float>()))
            .Callback(() => observed.Add(DrawOpacity.Current));
        backdrop.Setup(provider => provider.CollectBackdrops(fixture.Render.Object, It.IsAny<ElementBounds>()))
            .Callback(() => observed.Add(DrawOpacity.Current));
        using var view = ViewFactory.Create(overlay.Object, fixture.Api.Object, role, fixture.Failures.Add);
        Assert.IsType<LegacyView>(view);
        using var composer = fixture.CreateComposer();
        view.AddTo(composer, ElementBounds.Fixed(7, 11, 35, 43), "external");
        composer.Compose(false);
        var clip = new RectangleF(10, 13, 19, 27);
        view.Prepare(fixture.Context); view.LayoutCompleted(clip); view.SetOpacity(opacity);
        float previousOpacity = DrawOpacity.Current;
        view.CollectBackdrops(fixture.Render.Object);
        composer.Render(.2f);
        var element = Assert.IsType<LegacyElement>(composer.GetElement("external:legacy"));
        overlay.Verify(overlay => overlay.Draw(fixture.Render.Object, element.Bounds, clip,
            role == NativeViewRole.SourceTrace ? 0 : .2f), Times.Once);
        Assert.Equal(new[] { opacity, opacity }, observed);
        Assert.Equal(previousOpacity, DrawOpacity.Current);
        fixture.Render.Verify(render => render.PushScissor(It.Is<ElementBounds>(bounds => bounds.renderX == clip.X
            && bounds.renderY == clip.Y && bounds.OuterWidth == clip.Width && bounds.OuterHeight == clip.Height), true), Times.Once);
        overlay.Verify(overlay => overlay.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
        overlay.Verify(overlay => overlay.Refresh(), Times.Never);
    }
    /// <summary>Adapter and composer retirement do not acquire registry-owned controller cleanup.</summary>
    [Fact]
    public void ExistingRegistrationOverloadsKeepRegistryLifetimeOwnership()
    {
        using var fixture = new HudNativeComponentFixture();
        var overlay = new Mock<IHudOverlay>();
        using var registry = new HudOverlayRegistry();
        registry.RegisterGroup(new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
        var oldConstructor = new HudOverlayRegistration("external:one", overlay.Object, () => true, "group", 5, 300, "external:hover", new SizeF(35, 43));
        var menuConstructor = new HudOverlayRegistration("external:two", new Mock<IHudOverlay>().Object,
            () => true, "group", null, 6, 300, "external:hover", new SizeF(35, 43));
        Assert.Equal(oldConstructor.ReservedSize, menuConstructor.ReservedSize);
        using var handle = registry.Register(oldConstructor);
        registry.BeginSession(fixture.Api.Object);
        using var view = ViewFactory.Create(overlay.Object, fixture.Api.Object, NativeViewRole.Live, fixture.Failures.Add);
        using var composer = fixture.CreateComposer();
        view.AddTo(composer, ElementBounds.Fixed(0, 0, 35, 43), "external");
        view.Dispose(); composer.Dispose();
        overlay.Verify(overlay => overlay.EndSession(), Times.Never);
        overlay.Verify(overlay => overlay.Dispose(), Times.Never);
        handle.Dispose();
        overlay.Verify(overlay => overlay.BeginSession(fixture.Api.Object), Times.Once);
        overlay.Verify(overlay => overlay.EndSession(), Times.Once);
        overlay.Verify(overlay => overlay.Dispose(), Times.Once);
    }
    /// <summary>Legacy errors restore opacity and clipping before reporting the feature failure.</summary>
    [Fact]
    public void ThrowingLegacyDrawRestoresStateAndReportsFailure()
    {
        using var fixture = new HudNativeComponentFixture();
        var overlay = new Mock<IHudOverlay>();
        overlay.Setup(overlay => overlay.Draw(It.IsAny<IRenderAPI>(), It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), It.IsAny<float>()))
            .Throws(new InvalidOperationException());
        using var view = ViewFactory.Create(overlay.Object, fixture.Api.Object, NativeViewRole.Live, fixture.Failures.Add);
        using var composer = fixture.CreateComposer();
        view.AddTo(composer, ElementBounds.Fixed(0, 0, 35, 43), "external"); composer.Compose(false);
        view.LayoutCompleted(new(0, 0, 50, 50)); view.SetOpacity(.4f);
        float previous = DrawOpacity.Current;
        composer.Render(.1f);
        Assert.Single(fixture.Failures); Assert.Equal(previous, DrawOpacity.Current);
        fixture.Render.Verify(render => render.PopScissor(), Times.Once);
        overlay.Verify(overlay => overlay.Dispose(), Times.Never);
    }
    #endregion
    #endregion
}
