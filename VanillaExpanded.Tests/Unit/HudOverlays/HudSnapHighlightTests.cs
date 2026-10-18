using System.Drawing;
using Cairo;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies directional feedback geometry and session texture ownership.</summary>
public sealed class HudSnapHighlightTests
{
    #region Public API
    /// <summary>Bands occupy the incoming item and touch its edge facing the destination.</summary>
    [Theory]
    [InlineData((int)HudSnapSide.Left, 40, 50, 100, 120, 5, 80, 30, 40)]
    [InlineData((int)HudSnapSide.Right, 40, 50, 100, 120, 145, 80, 30, 40)]
    [InlineData((int)HudSnapSide.Above, 40, 50, 100, 120, 60, 5, 30, 40)]
    [InlineData((int)HudSnapSide.Below, 40, 50, 100, 120, 60, 175, 30, 40)]
    public void BandUsesSharedSpanInsideIncomingFacingDestination(int direction,
        float targetX, float targetY, float targetWidth, float targetHeight,
        float incomingX, float incomingY, float incomingWidth, float incomingHeight)
    {
        var side = (HudSnapSide)direction;
        var destination = new RectangleF(targetX, targetY, targetWidth, targetHeight);
        var incoming = new RectangleF(incomingX, incomingY, incomingWidth, incomingHeight);
        var band = HudSnapHighlight.ConnectionBounds(destination, incoming, side);
        Assert.True(incoming.Contains(band));
        Assert.False(destination.IntersectsWith(band));
        Assert.True(band.Width > 0 && band.Height > 0);
        if (side is HudSnapSide.Left or HudSnapSide.Right)
        {
            Assert.Equal(incoming.Top, band.Top);
            Assert.Equal(incoming.Bottom, band.Bottom);
            Assert.Equal(side == HudSnapSide.Left ? incoming.Right : incoming.Left,
                side == HudSnapSide.Left ? band.Right : band.Left);
        }
        else
        {
            Assert.Equal(incoming.Left, band.Left);
            Assert.Equal(incoming.Right, band.Right);
            Assert.Equal(side == HudSnapSide.Above ? incoming.Bottom : incoming.Top,
                side == HudSnapSide.Above ? band.Bottom : band.Top);
        }
        Assert.Equal(RectangleF.Empty, HudSnapHighlight.ConnectionBounds(destination,
            new RectangleF(destination.Right + 1, destination.Bottom + 1, 10, 10), side));
    }

    /// <summary>Geometry changes reuse directional textures and cleanup releases each exactly once.</summary>
    [Fact]
    public void DirectionalTexturesAreCachedAndGuidesDoNotAllocate()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        int nextTexture = 10;
        gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true)).Returns(() => nextTexture++);
        var bounds = new RectangleF(30, 40, 100, 100);
        var highlight = new HudSnapHighlight(api.Object);
        highlight.DrawGuide(bounds);
        gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
        foreach (var side in Enum.GetValues<HudSnapSide>())
        {
            highlight.DrawConnection(bounds, bounds, side);
            highlight.DrawConnection(bounds, new RectangleF(40, 50, 80, 80), side);
        }
        int directions = Enum.GetValues<HudSnapSide>().Length;
        gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Exactly(directions));
        Assert.Equal(directions * 2, render.Invocations.Count(value => value.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)));
        highlight.Dispose();
        highlight.Dispose();
        for (int texture = 10; texture < nextTexture; texture++)
            gui.Verify(value => value.DeleteTexture(texture), Times.Once);
    }
    #endregion
}

