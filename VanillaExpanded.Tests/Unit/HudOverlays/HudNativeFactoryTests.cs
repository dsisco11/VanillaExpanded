using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.WindSpeed;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises native factory selection and controller ownership.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudNativeFactoryTests
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
    /// <summary>Missing native capability is an explicit factory error.</summary>
    [Fact]
    public void MissingCapabilityIsRejected()
    {
        using var fixture = new HudNativeComponentFixture();
        Assert.Throws<InvalidOperationException>(() => ViewFactory.Create(Mock.Of<IHudOverlay>(),
            fixture.Api.Object, NativeViewRole.Live, fixture.Failures.Add));
        using var overlay = new WindSpeedOverlay();
        // This check continues to apply after this particular built-in gains the optional capability.
        if ((IHudOverlay)overlay is INativePresentation)
        {
            using var view = ViewFactory.Create(overlay, fixture.Api.Object, NativeViewRole.Live, fixture.Failures.Add);
            Assert.NotNull(view);
        }
        else
            Assert.Throws<InvalidOperationException>(() => ViewFactory.Create(overlay, fixture.Api.Object,
                NativeViewRole.Live, fixture.Failures.Add));
    }
    /// <summary>A broken native provider is not silently retried through the old drawing contract.</summary>
    [Fact]
    public void FailedNativeFactoryNeverCallsManualDrawing()
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
    #endregion
}
