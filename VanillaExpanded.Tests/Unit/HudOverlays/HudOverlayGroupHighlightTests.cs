using System.Drawing;
using Cairo;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Rendering;
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
            highlight.Draw(bounds, false);
            gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
            highlight.Draw(bounds, true);
            highlight.Draw(bounds, true);
            render.Verify(value => value.Render2DTexturePremultipliedAlpha(37,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, It.IsAny<float>(), It.IsAny<Vec4f>()), Times.Exactly(2));
            gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Once);
            render.Verify(value => value.RenderRectangle(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(),
                It.IsAny<float>(), It.IsAny<float>(), It.IsAny<int>()), Times.Never);
        }
        gui.Verify(value => value.DeleteTexture(37), Times.Once);
    }

    /// <summary>Both permission cues stay inside supplied bounds without allocating fill textures.</summary>
    [Theory]
    [InlineData(30, 50, 105, 250, true, true)]

    [InlineData(3, 7, 2, 3, true, false)]

    public void MovableOutlineUsesContainedSegments(float x, float y, float width, float height, bool movable, bool verifyGaps)
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render).Returns(render.Object);
        var bounds = new RectangleF(x, y, width, height);
        using var highlight = new HudOverlayGroupHighlight(api.Object);
        highlight.DrawOutline(bounds, movable);
        Assert.NotEmpty(render.Invocations);
        foreach (var call in render.Invocations)
        {
            Assert.Equal(nameof(IRenderAPI.RenderRectangle), call.Method.Name);
            var segment = new RectangleF((float)call.Arguments[0], (float)call.Arguments[1],
                (float)call.Arguments[3], (float)call.Arguments[4]);
            Assert.True(bounds.Contains(segment));
            Assert.NotEqual(bounds, segment);
        }
        if (verifyGaps)
        {
            var topSegments = render.Invocations
                .Where(call => (float)call.Arguments[1] == bounds.Top)
                .Select(call => ((float)call.Arguments[0], (float)call.Arguments[0] + (float)call.Arguments[3]))
                .OrderBy(segment => segment.Item1).ToArray();
            float coveredUntil = bounds.Left;
            bool gap = false;
            foreach (var segment in topSegments)
            {
                gap |= segment.Item1 > coveredUntil;
                coveredUntil = Math.Max(coveredUntil, segment.Item2);
            }
            Assert.True(gap || coveredUntil < bounds.Right);
        }
        gui.Verify(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), true), Times.Never);
    }
    /// <summary>Fixed frames forward the supplied bounds and active opacity to one owned shader renderer.</summary>
    [Theory]
    [InlineData(13.5f, 29.25f, 91.5f, 180.75f, .63f)]
    [InlineData(3, 7, 2, 3, .27f)]
    public void FixedOutlineForwardsOneShaderDrawAndDisposesRenderer(float x, float y, float width, float height, float opacity)
    {
        var api = new Mock<ICoreClientAPI>();
        var render = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Render).Returns(render.Object);
        var dashed = new Mock<IHudOverlayDashedBorder>();
        var bounds = new RectangleF(x, y, width, height);
        using (var highlight = new HudOverlayGroupHighlight(api.Object, dashed.Object))
        {
            using (new HudOverlayDrawOpacity(new object(), opacity)) highlight.DrawOutline(bounds, false);
            dashed.Verify(value => value.Draw(bounds, opacity), Times.Once);
            render.Verify(value => value.RenderRectangle(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(),
                It.IsAny<float>(), It.IsAny<float>(), It.IsAny<int>()), Times.Never);
            highlight.DrawOutline(RectangleF.Empty, false);
            dashed.Verify(value => value.Draw(It.IsAny<RectangleF>(), It.IsAny<float>()), Times.Once);
        }
        dashed.Verify(value => value.Dispose(), Times.Once);
    }
    #endregion
}
