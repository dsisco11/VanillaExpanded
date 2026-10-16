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
    /// <summary>Scale positions share the liquid's calibrated clockwise arc and endpoint clamping.</summary>
    [Theory]
    [InlineData(-55, 150)] [InlineData(-40, 150)] [InlineData(0, 246)]
    [InlineData(9, 267.6)] [InlineData(60, 390)] [InlineData(75, 390)]
    public void CalibratedAngles(float celsius, double expected)
    {
        Assert.Equal(expected, EnvironmentalTemperatureGaugeLayout.Angle(celsius) * 180 / Math.PI, 4);
        var point = EnvironmentalTemperatureGaugeLayout.Position(celsius, 164);
        Assert.Equal(164, Math.Sqrt(Math.Pow(point.X - 256, 2) + Math.Pow(point.Y - 256, 2)), 4);
    }

    /// <summary>The liquid fills clockwise, including the wrap past the top, with an antialiased boundary.</summary>
    [Theory]
    [InlineData(.2f)] [InlineData(.4f)] [InlineData(.49f)] [InlineData(.8f)]
    public void AngularCoverageAndBoundary(float fill)
    {
        float degrees = -40 + 100 * fill;
        var before = EnvironmentalTemperatureGaugeLayout.Position(degrees - 2, 196);
        var edge = EnvironmentalTemperatureGaugeLayout.Position(degrees, 196);
        var after = EnvironmentalTemperatureGaugeLayout.Position(degrees + 2, 196);
        Assert.Equal(1, EnvironmentalTemperatureGaugeLayout.Coverage(before.X, before.Y, fill));
        Assert.InRange(EnvironmentalTemperatureGaugeLayout.Coverage(edge.X, edge.Y, fill), .499, .501);
        Assert.Equal(0, EnvironmentalTemperatureGaugeLayout.Coverage(after.X, after.Y, fill));
        Assert.Equal(1, EnvironmentalTemperatureGaugeLayout.Coverage(110, 384, 0));
        Assert.Equal(0, EnvironmentalTemperatureGaugeLayout.Coverage(256, 60, 0));
        Assert.Equal(1, EnvironmentalTemperatureGaugeLayout.Coverage(415, 360, 1));
    }

    /// <summary>Alpha masking preserves source RGB and reuses GPU storage until reset, without per-frame sampling.</summary>
    [Fact]
    public void PixelsAndTextureLifetime()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var renderer = new Mock<IRenderAPI>(); api.SetupGet(value => value.Render).Returns(renderer.Object);
        int sourceLoads = 0;
        var source = Enumerable.Repeat(unchecked((int)0xffb02b19), 512 * 512).ToArray();
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
        Assert.Equal(255u, (uint)uploads[0][384 * 512 + 110] >> 24);
        Assert.Equal(0u, (uint)uploads[0][60 * 512 + 256] >> 24);
        Assert.Equal(0xb02b19, uploads[0][60 * 512 + 256] & 0xffffff);
        mask.Prepare(context, 1);
        Assert.Equal(source, uploads[1]); Assert.Same(targets[0], targets[1]);
        mask.Draw(renderer.Object, new RectangleF(10, 20, 90, 90));
        renderer.Verify(value => value.RenderTexture(77, 10, 20, 90, 90, 51, It.IsAny<Vec4f>()), Times.Once);
        mask.Reset(); Assert.True(targets[0].Disposed);
        Assert.All(source, pixel => Assert.Equal(unchecked((int)0xffb02b19), pixel));
    }
    #endregion

    #region Private
    /// <summary>Observes the native upload's ref texture parameter without creating a graphics context.</summary>
    private delegate void Upload(int[] pixels, bool linear, int clamp, ref LoadedTexture texture);
    #endregion
}
