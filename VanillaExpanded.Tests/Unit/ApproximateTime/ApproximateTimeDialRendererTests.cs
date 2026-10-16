using System.Drawing;
using Moq;
using VanillaExpanded.ApproximateTime;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks spindle anchoring, clockwise hour orientation, and matrix restoration without a graphics context.</summary>
public sealed class ApproximateTimeDialRendererTests
{
    #region Public API
    /// <summary>Every cardinal hour leaves the spindle fixed while rotating the quad in screen space at different sizes.</summary>
    [Theory]
    [InlineData(0, 0, -1, 45)]
    [InlineData(Math.PI / 2, 1, 0, 90)]
    [InlineData(-Math.PI, 0, 1, 180)]
    [InlineData(-Math.PI / 2, -1, 0, 90)]
    public void PivotAndClockwiseHours(double rotation, double directionX, double directionY, float size)
    {
        var renderer = new Mock<IRenderAPI>();
        var matrix = new Matrixf().Identity();
        var bounds = new RectangleF(23, 41, size, size);
        double pivotX = bounds.X + size / 2d, pivotY = bounds.Y + size * 250d / 512;
        var order = new List<string>();
        renderer.Setup(value => value.GlPushMatrix()).Callback(() => order.Add("push"));
        renderer.Setup(value => value.GlPopMatrix()).Callback(() => order.Add("pop"));
        renderer.Setup(value => value.GlTranslate(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
            .Callback<double, double, double>((x, y, z) => { matrix.Translate(x, y, z); order.Add("translate"); });
        renderer.Setup(value => value.GlRotate(It.IsAny<float>(), 0, 0, 1))
            .Callback<float, float, float, float>((angle, _, _, _) => { matrix.RotateZDeg(angle); order.Add("rotate"); });
        renderer.Setup(value => value.RenderTexture(7, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), 52, It.IsAny<Vec4f>()))
            .Callback<int, double, double, double, double, float, Vec4f>((_, x, y, width, height, _, tint) =>
            {
                order.Add("draw");
                var spindle = matrix.TransformVector(new Vec4f((float)(x + width / 2), (float)(y + height * 250 / 512), 0, 1));
                Assert.Equal(pivotX, spindle.X, 4); Assert.Equal(pivotY, spindle.Y, 4);
                var top = matrix.TransformVector(new Vec4f((float)(x + width / 2), (float)y, 0, 1));
                Assert.Equal(pivotX + directionX * height * 250 / 512, top.X, 4);
                Assert.Equal(pivotY + directionY * height * 250 / 512, top.Y, 4);
                Assert.Equal(.35f, tint.W); Assert.Equal(1, tint.X); Assert.Equal(size, width); Assert.Equal(size, height);
            });
        new ApproximateTimeDialRenderer().Draw(renderer.Object, 7, bounds, .35f, (float)rotation);
        Assert.Equal(new[] { "push", "translate", "rotate", "draw", "pop" }, order);
    }

    /// <summary>A failed native texture draw cannot leave the pointer transform on the host's matrix stack.</summary>
    [Fact]
    public void FailedDrawRestoresMatrix()
    {
        var renderer = new Mock<IRenderAPI>();
        renderer.Setup(value => value.RenderTexture(7, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), 52, It.IsAny<Vec4f>())).Throws(new InvalidOperationException("draw"));
        Assert.Throws<InvalidOperationException>(() => new ApproximateTimeDialRenderer().Draw(renderer.Object, 7, new(10, 20, 90, 90), 1, .5f));
        renderer.Verify(value => value.GlPushMatrix(), Times.Once);
        renderer.Verify(value => value.GlPopMatrix(), Times.Once);
    }
    #endregion
}
