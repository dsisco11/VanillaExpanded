using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks the validation shader's analytic sampled-area and silhouette constraints across every supported subdivision.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorDemonstrationTests
{
    #region Public API
    /// <summary>Shader-specific declaration validation rejects nonfinite/out-of-range strength and zeros unused lanes.</summary>
    [Fact]
    public void Declaration_ValidatesStrengthAndDocumentsUnusedParameters()
    {
        foreach (float strength in new[] { float.NaN, float.PositiveInfinity, -0.1f, 1.1f })
            Assert.Throws<ArgumentOutOfRangeException>(() => ItemSlotIndicatorDemonstration.Create(strength));
        var effect = ItemSlotIndicatorDemonstration.Create();
        Assert.True(effect.NeedsCameraMotion);
        Assert.Equal(new System.Numerics.Vector4(1, 0, 0, 0), effect.Parameters);
        Assert.Equal(16, effect.SegmentCount);
    }

    /// <summary>Both sampled basis functions have zero trapezoidal mean; their bounded mix preserves fill area and containment.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.0001f)]
    [InlineData(0.01f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.99f)]
    [InlineData(0.9999f)]
    [InlineData(1f)]
    public void SampledSilhouette_PreservesBottomAreaAndNearLimitBounds(float fill)
    {
        double bound = Math.Min(0.1, Math.Min(0.25 * fill, 0.25 * (1 - fill)));
        for (int segments = 2; segments <= 64; segments++)
            foreach (double time in new[] { 0, 0.25, 1.0, 3.0, 63.99, 64.0 })
                foreach (double motionX in new[] { -1.0, 0, 1.0 })
                    foreach (double motionY in new[] { -1.0, 0, 1.0 })
                    {
                        double sum = 0;
                        for (int sample = 0; sample <= segments; sample++)
                        {
                            double u = (double)sample / segments;
                            double deformation = Deformation(u, time, motionX, motionY, bound);
                            Assert.InRange(deformation, -bound - 1e-12, bound + 1e-12);
                            Assert.InRange(fill + deformation, 0, 1);
                            sum += deformation * (sample == 0 || sample == segments ? 0.5 : 1);
                            if (fill is 0 or 1) Assert.Equal(0, deformation);
                        }
                        Assert.InRange(Math.Abs(sum / segments), 0, 1e-12);
                    }
        Assert.NotEqual(Deformation(0.25, 0, 0, 0, 0.1), Deformation(0.25, 1, 0, 0, 0.1));
        Assert.Equal(Deformation(0.25, 0, 0, 0, 0.1), Deformation(0.25, 64, 0, 0, 0.1), 12);
    }
    #endregion

    #region Private
    /// <summary>Evaluates the documented analytic shader equation for sampled contract verification; never used by runtime rendering.</summary>
    private static double Deformation(double u, double time, double motionX, double motionY, double bound) =>
        bound * (0.5 * Math.Sin(2 * Math.PI * u) * Math.Cos(2 * Math.PI * time / 4) * (0.75 + 0.25 * motionY)
            + 0.5 * (2 * u - 1) * motionX);
    #endregion
}
