using System.Drawing;
using Cairo;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies directional feedback geometry and session texture ownership.</summary>
public sealed class HudSnapHighlightTests
{
    #region Public API
    /// <summary>Grouping guides cross both resolved footprints at the alignment that the saved pair will use.</summary>
    [Theory]
    [InlineData((int)HudSnapSide.Left, (int)HudOverlayCrossAlignment.Start)]
    [InlineData((int)HudSnapSide.Left, (int)HudOverlayCrossAlignment.Center)]
    [InlineData((int)HudSnapSide.Left, (int)HudOverlayCrossAlignment.End)]
    [InlineData((int)HudSnapSide.Right, (int)HudOverlayCrossAlignment.Start)]
    [InlineData((int)HudSnapSide.Right, (int)HudOverlayCrossAlignment.Center)]
    [InlineData((int)HudSnapSide.Right, (int)HudOverlayCrossAlignment.End)]
    [InlineData((int)HudSnapSide.Above, (int)HudOverlayCrossAlignment.Start)]
    [InlineData((int)HudSnapSide.Above, (int)HudOverlayCrossAlignment.Center)]
    [InlineData((int)HudSnapSide.Above, (int)HudOverlayCrossAlignment.End)]
    [InlineData((int)HudSnapSide.Below, (int)HudOverlayCrossAlignment.Start)]
    [InlineData((int)HudSnapSide.Below, (int)HudOverlayCrossAlignment.Center)]
    [InlineData((int)HudSnapSide.Below, (int)HudOverlayCrossAlignment.End)]
    public void GroupingGuideShowsResolvedPairAlignment(int direction, int crossAlignment)
    {
        var side = (HudSnapSide)direction;
        var alignment = (HudOverlayCrossAlignment)crossAlignment;
        var tree = HudGroupingResolver.Insert(new() { Indicator = "target" }, new() { Indicator = "incoming" },
            null, side, alignment, gap: 7);
        var geometry = HudLayoutGeometry.Resolve(tree, new Dictionary<string, SizeF>
        {
            ["target"] = new(43, 61), ["incoming"] = new(79, 29)
        }, padding: 3);
        var target = geometry.Rectangles["target"];
        var incoming = geometry.Rectangles["incoming"];
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true)).Returns(10);
        using var highlight = new HudSnapHighlight(api.Object);
        highlight.DrawGrouping(target, incoming, side, alignment);
        var guide = Assert.Single(render.Invocations, value => value.Method.Name == nameof(IRenderAPI.RenderRectangle));
        float factor = crossAlignment / 2f;
        bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
        double coordinate = horizontal ? target.Top + factor * target.Height : target.Left + factor * target.Width;
        Assert.Equal(coordinate, Convert.ToDouble(guide.Arguments[horizontal ? 1 : 0]));
        Assert.Equal(coordinate, horizontal ? incoming.Top + factor * incoming.Height : incoming.Left + factor * incoming.Width);
        var span = RectangleF.Union(target, incoming);
        Assert.Equal(horizontal ? span.Left : span.Top, Convert.ToDouble(guide.Arguments[horizontal ? 0 : 1]));
        Assert.Equal(horizontal ? span.Width : span.Height, Convert.ToDouble(guide.Arguments[horizontal ? 3 : 4]));
        Assert.True(render.Invocations.ToList().FindIndex(value => value.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha))
            < render.Invocations.ToList().IndexOf(guide));
        highlight.DrawGrouping(target, incoming, side, alignment);
        gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Once);
    }

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
