using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks continuous border geometry using caller-supplied footprints rather than presentation styling.</summary>
public sealed class HudOverlayHoverBorderTests
{
    #region Public API
    /// <summary>Every border rectangle stays inside its input and reaches one complete outer edge.</summary>
    [Theory]
    [InlineData(12, 18, 41, 23)]
    [InlineData(-20, -8, 0.4f, 0.7f)]
    public void EdgesCoverAndRemainInsideSuppliedFootprint(float x, float y, float width, float height)
    {
        var bounds = new RectangleF(x, y, width, height);
        var rectangles = new List<RectangleF>();
        var renderer = new Mock<IRenderAPI>();
        renderer.Setup(value => value.RenderRectangle(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(),
            It.IsAny<float>(), It.IsAny<float>(), It.IsAny<int>()))
            .Callback<float, float, float, float, float, int>((left, top, _, w, h, _) => rectangles.Add(new(left, top, w, h)));
        HudOverlayHoverBorder.Draw(renderer.Object, bounds);
        Assert.NotEmpty(rectangles);
        Assert.All(rectangles, rectangle => Assert.True(bounds.Contains(rectangle)));
        Assert.Contains(rectangles, rectangle => rectangle.Left == bounds.Left && rectangle.Right == bounds.Right && rectangle.Top == bounds.Top);
        Assert.Contains(rectangles, rectangle => rectangle.Left == bounds.Left && rectangle.Right == bounds.Right && rectangle.Bottom == bounds.Bottom);
        Assert.Contains(rectangles, rectangle => rectangle.Top == bounds.Top && rectangle.Bottom == bounds.Bottom && rectangle.Left == bounds.Left);
        Assert.Contains(rectangles, rectangle => rectangle.Top == bounds.Top && rectangle.Bottom == bounds.Bottom && rectangle.Right == bounds.Right);
    }

    /// <summary>Empty footprints do not submit native drawing calls.</summary>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(20, 0)]
    [InlineData(-1, 20)]
    public void EmptyFootprintDoesNotDraw(float width, float height)
    {
        var renderer = new Mock<IRenderAPI>();
        HudOverlayHoverBorder.Draw(renderer.Object, new RectangleF(0, 0, width, height));
        renderer.VerifyNoOtherCalls();
    }
    #endregion
}
