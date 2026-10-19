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
        var matrices = new Stack<Matrixf>();
        var bounds = new RectangleF(23, 41, size, size);
        double pivotX = 0, pivotY = 0;
        var order = new List<string>();
        var layers = new List<(double X, double Y, float Depth, float Opacity)>();
        renderer.Setup(value => value.GlPushMatrix()).Callback(() => { matrices.Push(matrix.Clone()); order.Add("push"); });
        renderer.Setup(value => value.GlPopMatrix()).Callback(() => { matrix = matrices.Pop(); order.Add("pop"); });
        renderer.Setup(value => value.GlTranslate(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
            .Callback<double, double, double>((x, y, z) => { pivotX = x; pivotY = y; matrix.Translate(x, y, z); order.Add("translate"); });
        renderer.Setup(value => value.GlRotate(It.IsAny<float>(), 0, 0, 1))
            .Callback<float, float, float, float>((angle, _, _, _) => { matrix.RotateZDeg(angle); order.Add("rotate"); });
        renderer.Setup(value => value.RenderTexture(7, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback<int, double, double, double, double, float, Vec4f>((_, x, y, width, height, depth, tint) =>
            {
                order.Add("draw");
                // Verify rotation around the declared spindle, independently of its artwork registration coordinates.
                Assert.InRange(0d, x, x + width); Assert.InRange(0d, y, y + height);
                var spindle = matrix.TransformVector(new Vec4f(0, 0, 0, 1));
                Assert.Equal(pivotX, spindle.X, 4); Assert.Equal(pivotY, spindle.Y, 4);
                var top = matrix.TransformVector(new Vec4f(0, (float)(-height / 2), 0, 1));
                Assert.Equal(pivotX + directionX * height / 2, top.X, 4);
                Assert.Equal(pivotY + directionY * height / 2, top.Y, 4);
                Assert.Equal(size, width); Assert.Equal(size, height);
                layers.Add((pivotX, pivotY, depth, tint.W));
            });
        new ApproximateTimeDialRenderer().Draw(renderer.Object, 7, bounds, .35f, (float)rotation);
        Assert.Equal(Enumerable.Repeat(new[] { "push", "translate", "rotate", "draw", "pop" }, 2).SelectMany(value => value), order);
        Assert.Equal(2, layers.Count);
        Assert.True(layers[0].Depth < layers[1].Depth);
        Assert.Equal(size * 6d / 512, layers[0].X - layers[1].X, 4);
        Assert.Equal(size * 8d / 512, layers[0].Y - layers[1].Y, 4);
        Assert.InRange(layers[0].Opacity, float.Epsilon, layers[1].Opacity);
        Assert.Equal(.35f, layers[1].Opacity);
        Assert.Empty(matrices);
        Assert.Equal(new Matrixf().Identity().Values, matrix.Values);
    }

    /// <summary>Fading the overlay reduces both hand layers by the same proportion.</summary>
    [Fact]
    public void ShadowAndHandShareOverlayFade()
    {
        var renderer = new Mock<IRenderAPI>();
        var opacity = new List<float>();
        renderer.Setup(value => value.RenderTexture(7, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback<int, double, double, double, double, float, Vec4f>((_, _, _, _, _, _, tint) => opacity.Add(tint.W));
        var dial = new ApproximateTimeDialRenderer();
        dial.Draw(renderer.Object, 7, new(10, 20, 90, 90), 1, .5f);
        dial.Draw(renderer.Object, 7, new(10, 20, 90, 90), .5f, .5f);
        Assert.Equal(opacity[0] / 2, opacity[2]);
        Assert.Equal(opacity[1] / 2, opacity[3]);
    }

    /// <summary>A failed native texture draw cannot leave the pointer transform on the host's matrix stack.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FailedDrawRestoresMatrix(int failingLayer)
    {
        var renderer = new Mock<IRenderAPI>();
        int draws = 0, stackDepth = 0;
        renderer.Setup(value => value.GlPushMatrix()).Callback(() => stackDepth++);
        renderer.Setup(value => value.GlPopMatrix()).Callback(() => stackDepth--);
        renderer.Setup(value => value.RenderTexture(7, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback(() => { if (++draws == failingLayer) throw new InvalidOperationException("draw"); });
        Assert.Throws<InvalidOperationException>(() => new ApproximateTimeDialRenderer().Draw(renderer.Object, 7, new(10, 20, 90, 90), 1, .5f));
        Assert.Equal(failingLayer, draws);
        Assert.Equal(0, stackDepth);
        renderer.Verify(value => value.GlPushMatrix(), Times.Exactly(failingLayer));
        renderer.Verify(value => value.GlPopMatrix(), Times.Exactly(failingLayer));
    }
    #endregion
}
