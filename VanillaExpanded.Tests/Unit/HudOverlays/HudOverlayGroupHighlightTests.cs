using System.Drawing;
using Cairo;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks forwarded group geometry and reusable fill ownership without a graphics context.</summary>
public sealed class HudOverlayGroupHighlightTests
{
    #region Public API
    /// <summary>Only hover allocates a reusable fill and forwards the caller's supplied group bounds.</summary>
    [Fact]
    public void HoverUsesExactBoundsAndCachesItsSolidPixel()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true)).Returns(37);
        var bounds = new RectangleF(13.5f, 29.25f, 91.5f, 180.75f);
        using (var highlight = new HudOverlayGroupHighlight(api.Object))
        {
            highlight.Draw(bounds, false, false);
            gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
            highlight.Draw(bounds, true, false);
            highlight.Draw(bounds, true, false);
            render.Verify(value => value.Render2DTexturePremultipliedAlpha(37,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, It.IsAny<float>(), It.IsAny<Vec4f>()), Times.Exactly(2));
            gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Once);
            render.Verify(value => value.RenderRectangle(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(),
                It.IsAny<float>(), It.IsAny<float>(), It.IsAny<int>()), Times.Never);
        }
        gui.Verify(value => value.DeleteTexture(37), Times.Once);
    }

    /// <summary>Movable bounds use native outline drawing and never allocate or draw a filled texture when not hovered.</summary>
    [Fact]
    public void UnhoveredMovableUsesOnlyOutlineAtSuppliedBounds()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        var bounds = new RectangleF(30, 50, 105, 250);
        using var highlight = new HudOverlayGroupHighlight(api.Object);
        highlight.Draw(bounds, false, true);
        render.Verify(value => value.RenderRectangle(bounds.X, bounds.Y, It.IsAny<float>(), bounds.Width, bounds.Height, It.IsAny<int>()), Times.Once);
        Assert.Single(render.Invocations);
        gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
    }
    #endregion
}
