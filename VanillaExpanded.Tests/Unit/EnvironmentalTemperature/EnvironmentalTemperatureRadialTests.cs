using System.Drawing;
using Moq;
using VanillaExpanded.EnvironmentalTemperature;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.EnvironmentalTemperature;

/// <summary>Checks radial calibration, authored-color preservation, angular coverage, and mask resource reuse.</summary>
public sealed class EnvironmentalTemperatureRadialTests
{
    #region Public API
    /// <summary>Angles follow clamped temperature progress, while positions preserve the supplied radius and angle direction.</summary>
    [Theory]
    [InlineData(-55, 0)] [InlineData(-40, 0)] [InlineData(0, .4)]
    [InlineData(9, .49)] [InlineData(60, 1)] [InlineData(75, 1)]
    public void AnglesFollowClampedTemperatureProgress(float celsius, double progress)
    {
        double start = EnvironmentalTemperatureGaugeLayout.Angle(-40);
        double end = EnvironmentalTemperatureGaugeLayout.Angle(60);
        Assert.True(end > start);
        double angle = EnvironmentalTemperatureGaugeLayout.Angle(celsius);
        Assert.Equal(start + (end - start) * progress, angle, 4);
        const float radius = 164;
        var origin = EnvironmentalTemperatureGaugeLayout.Position(celsius, 0);
        var point = EnvironmentalTemperatureGaugeLayout.Position(celsius, radius);
        Assert.InRange(Math.Sqrt(Math.Pow(point.X - origin.X, 2) + Math.Pow(point.Y - origin.Y, 2)) - radius, -.001, .001);
        Assert.InRange(point.X - origin.X - Math.Cos(angle) * radius, -.001, .001);
        Assert.InRange(point.Y - origin.Y - Math.Sin(angle) * radius, -.001, .001);
    }

    /// <summary>The liquid fills clockwise, including the wrap past the top, with an antialiased boundary.</summary>
    [Theory]
    [InlineData(.2f)] [InlineData(.4f)] [InlineData(.49f)] [InlineData(.8f)]
    public void AngularCoverageAndBoundary(float fill)
    {
        float degrees = -40 + 100 * fill;
        var before = EnvironmentalTemperatureGaugeLayout.Position(degrees - 2, EnvironmentalTemperatureGaugeLayout.TubeRadius);
        var edge = EnvironmentalTemperatureGaugeLayout.Position(degrees, EnvironmentalTemperatureGaugeLayout.TubeRadius);
        var after = EnvironmentalTemperatureGaugeLayout.Position(degrees + 2, EnvironmentalTemperatureGaugeLayout.TubeRadius);
        float beforeCoverage = EnvironmentalTemperatureGaugeLayout.Coverage(before.X, before.Y, fill);
        float edgeCoverage = EnvironmentalTemperatureGaugeLayout.Coverage(edge.X, edge.Y, fill);
        float afterCoverage = EnvironmentalTemperatureGaugeLayout.Coverage(after.X, after.Y, fill);
        Assert.True(beforeCoverage > edgeCoverage);
        Assert.True(edgeCoverage > afterCoverage);
    }

    /// <summary>Alpha masking preserves source RGB and reuses GPU storage until reset, without per-frame sampling.</summary>
    [Fact]
    public void PixelsAndTextureLifetime()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var renderer = new Mock<IRenderAPI>(); api.SetupGet(value => value.Render).Returns(renderer.Object);
        int sourceLoads = 0;
        var source = Enumerable.Repeat(unchecked((int)0xffb02b19), 512 * 512).ToArray();
        var original = (int[])source.Clone();
        var uploads = new List<int[]>(); var targets = new List<LoadedTexture>();
        renderer.Setup(value => value.LoadOrUpdateTextureFromBgra(It.IsAny<int[]>(), true, 0, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((int[] pixels, bool _, int _, ref LoadedTexture texture) =>
            {
                uploads.Add((int[])pixels.Clone()); targets.Add(texture); texture.TextureId = 77;
            }));
        using var mask = new EnvironmentalTemperatureLiquidMask(_ => { sourceLoads++; return source; }, texture => texture);
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        mask.Prepare(context, 0); mask.Prepare(context, 0);
        mask.Prepare(new(api.Object, 2, "en"), 0);
        Assert.Single(uploads); Assert.Equal(1, sourceLoads);
        Assert.Contains(uploads[0], pixel => (uint)pixel >> 24 == 0);
        Assert.Contains(uploads[0], pixel => (uint)pixel >> 24 > 0);
        for (int index = 0; index < source.Length; index++)
            Assert.Equal(source[index] & 0xffffff, uploads[0][index] & 0xffffff);
        mask.Prepare(context, 1);
        Assert.Equal(source, uploads[1]); Assert.Same(targets[0], targets[1]);
        var suppliedBounds = new RectangleF(10, 20, 90, 90);
        mask.Draw(renderer.Object, suppliedBounds);
        renderer.Verify(value => value.RenderTexture(77, suppliedBounds.X, suppliedBounds.Y,
            suppliedBounds.Width, suppliedBounds.Height, It.IsAny<float>(), It.IsAny<Vec4f>()), Times.Once);
        mask.Reset(); Assert.True(targets[0].Disposed);
        Assert.Equal(original, source);
    }
    #endregion

    #region Private
    /// <summary>Observes the native upload's ref texture parameter without creating a graphics context.</summary>
    private delegate void Upload(int[] pixels, bool linear, int clamp, ref LoadedTexture texture);
    #endregion
}
