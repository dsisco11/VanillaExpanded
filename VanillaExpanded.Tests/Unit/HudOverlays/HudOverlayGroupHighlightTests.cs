using System.Drawing;
using Cairo;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks exact group highlight geometry, shared orange colors and reusable fill ownership without a graphics context.</summary>
public sealed class HudOverlayGroupHighlightTests
{
    #region Public API
    /// <summary>Only hover allocates a single white pixel and draws a premultiplied translucent orange fill at exact supplied bounds.</summary>
    [Fact]
    public void HoverUsesExactBoundsAndCachesItsSolidPixel()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true)).Returns((ImageSurface surface, bool _) =>
        {
            surface.Flush();
            Assert.Equal(1, surface.Width); Assert.Equal(1, surface.Height);
            Assert.All(surface.Data, channel => Assert.Equal(255, channel));
            return 37;
        });
        var bounds = new RectangleF(13.5f, 29.25f, 91.5f, 180.75f);
        using (var highlight = new HudOverlayGroupHighlight(api.Object))
        {
            highlight.Draw(bounds, false, false);
            gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
            highlight.Draw(bounds, true, false);
            highlight.Draw(bounds, true, false);
            render.Verify(value => value.Render2DTexturePremultipliedAlpha(37,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, 0,
                It.Is<Vec4f>(color => Math.Abs(color.A - .45f) < .00001f
                    && Math.Abs(color.R - RadialMenuWedgeStyle.HoverFill.X * .45f) < .00001f
                    && Math.Abs(color.G - RadialMenuWedgeStyle.HoverFill.Y * .45f) < .00001f
                    && Math.Abs(color.B - RadialMenuWedgeStyle.HoverFill.Z * .45f) < .00001f)), Times.Exactly(2));
            gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Once);
            render.Verify(value => value.RenderRectangle(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(),
                It.IsAny<float>(), It.IsAny<float>(), It.IsAny<int>()), Times.Never);
        }
        gui.Verify(value => value.DeleteTexture(37), Times.Once);
    }

    /// <summary>Movable bounds use native outline drawing and never allocate or draw a filled texture when not hovered.</summary>
    [Fact]
    public void UnhoveredMovableUsesOnlyExactOrangeOutline()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        var bounds = new RectangleF(30, 50, 105, 250);
        var line = RadialMenuWedgeStyle.HoverBorder;
        using var highlight = new HudOverlayGroupHighlight(api.Object);
        highlight.Draw(bounds, false, true);
        render.Verify(value => value.RenderRectangle(bounds.X, bounds.Y, 1, bounds.Width, bounds.Height, It.IsAny<int>()), Times.Once);
        int color = (int)Assert.Single(render.Invocations).Arguments[5];
        // Decode through the engine's public color contract so a matching but incorrect local bit packing cannot pass.
        Assert.Equal((byte)(line.X * 255), ColorUtil.ColorR(color));
        Assert.Equal((byte)(line.Y * 255), ColorUtil.ColorG(color));
        Assert.Equal((byte)(line.Z * 255), ColorUtil.ColorB(color));
        Assert.Equal(255, ColorUtil.ColorA(color));
        gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
    }
    #endregion
}
