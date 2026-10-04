using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks the liquid surface's analytic area, displacement, motion, and clock contracts without a GPU.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidSloshIndicatorEffectTests
{
    #region Public API
    /// <summary>Fixed appearance weights remain bounded and the resting wave has stronger displacement.</summary>
    [Fact]
    public void Definition_HasBoundedWeightsAndStrongerRestingWave()
    {
        var effect = LiquidSloshIndicatorEffect.Definition;
        var weights = effect.Parameters;
        Assert.True(effect.NeedsCameraMotion);
        Assert.Equal(ItemSlotIndicatorTopology.FillStrip, effect.Topology);
        Assert.Equal(16, effect.SegmentCount);
        Assert.InRange(weights.X, 0, 1);
        Assert.InRange(weights.Y, 0, 1);
        Assert.InRange(weights.Z, 0, 1);
        Assert.InRange(weights.W, 0, 1);
        Assert.InRange(weights.X + weights.Y + weights.Z + weights.W, 0, 1);
        Assert.True(weights.X >= 0.625f);
        Assert.Equal(new VanillaExpanded.ItemSlotIndicators.ItemSlotIndicatorDrawRange(0.15f, 0.85f),
            LiquidSloshIndicatorEffect.DrawRange);
    }

    /// <summary>Every supported subdivision retains average fill and containment, including endpoint warnings.</summary>
    [Fact]
    public void SampledSurface_PreservesAreaAndDisplacementBound()
    {
        double[] fills = [0, 0.0001, 0.01, 0.15, 0.5, 0.85, 0.99, 0.9999, 1];
        Vector2[] motions = [Vector2.Zero, new(-1, -1), new(-1, 1), new(1, -1), new(1, 1), new(0, 1)];
        // Trapezoidal weights match the actual equal-width triangle strip rather than a continuous integral alone.
        for (int segments = 2; segments <= ItemSlotIndicatorEffectDefinition.MaximumSegmentCount; segments++)
        foreach (double fill in fills)
        foreach (var motion in motions)
        foreach (float bob in new[] { -1f, 0, 1 })
        for (int phase = 0; phase < 16; phase++)
        {
            double integral = 0;
            double allowance = Math.Min(0.1, Math.Min(0.25 * fill, 0.25 * (1 - fill)));
            for (int sample = 0; sample <= segments; sample++)
            {
                double displacement = Displacement((double)sample / segments, fill, phase / 4.0, motion, segments, bob);
                Assert.InRange(Math.Abs(displacement), 0, allowance + 1e-12);
                Assert.InRange(fill + displacement, -1e-12, 1 + 1e-12);
                integral += displacement * (sample == 0 || sample == segments ? 0.5 : 1);
            }
            Assert.InRange(Math.Abs(integral / segments), 0, 1e-12);
        }
    }

    /// <summary>Waves wrap continuously, motion raises activity, and rightward turns raise the left surface.</summary>
    [Fact]
    public void MotionAndClock_ProduceIntendedSurfaceResponse()
    {
        for (int sample = 0; sample <= 16; sample++)
        {
            double u = sample / 16.0;
            Assert.Equal(Displacement(u, 0.5, 0.7, Vector2.One),
                Displacement(u, 0.5, 64.7, Vector2.One), 12);
            Assert.Equal(0, Displacement(u, 1, 0.7, Vector2.One));
            Assert.Equal(0, Displacement(u, 0, 0.7, Vector2.One));
        }
        Assert.True(Displacement(0.5, 0.5, 0, new(0, 1)) > Displacement(0.5, 0.5, 0, Vector2.Zero));
        Assert.True(Displacement(0, 0.5, 0, new(1, 0)) > Displacement(1, 0.5, 0, new(1, 0)));
        Assert.NotEqual(Displacement(0, 0.5, 0, Vector2.Zero), Displacement(0, 0.5, 0.5, Vector2.Zero));
    }

    /// <summary>The standing wave alternates center/side crests while a persistent meniscus raises both edges.</summary>
    [Fact]
    public void StandingWave_OscillatesSymmetricallyWithRaisedEdges()
    {
        Assert.True(Displacement(0.5, 0.5, 0, Vector2.Zero) > Displacement(0, 0.5, 0, Vector2.Zero));
        Assert.True(Displacement(0.5, 0.5, 1, Vector2.Zero) < Displacement(0, 0.5, 1, Vector2.Zero));
        // At a quarter cycle the standing wave vanishes, exposing the meniscus alone.
        Assert.True(Displacement(0, 0.5, 0.5, Vector2.Zero) > 0);
        Assert.True(Displacement(0.5, 0.5, 0.5, Vector2.Zero) < 0);
        for (int sample = 0; sample <= 16; sample++)
        {
            double u = sample / 16.0;
            Assert.Equal(Displacement(u, 0.5, 0.3, Vector2.Zero),
                Displacement(1 - u, 0.5, 0.3, Vector2.Zero), 12);
        }
    }

    /// <summary>Footstep bob changes the surface even without angular camera movement and remains clock-periodic.</summary>
    [Fact]
    public void CameraBob_AgitatesStandingWaveWithoutRotation()
    {
        double resting = Displacement(0.5, 0.5, 0.5, Vector2.Zero);
        double rising = Displacement(0.5, 0.5, 0.5, Vector2.Zero, bob: 1);
        double falling = Displacement(0.5, 0.5, 0.5, Vector2.Zero, bob: -1);
        Assert.True(rising < resting);
        Assert.True(falling > resting);
        Assert.Equal(rising, Displacement(0.5, 0.5, 64.5, Vector2.Zero, bob: 1), 12);
        Assert.Equal(resting, Displacement(0.5, 0.5, 2.5, Vector2.Zero), 12);
    }
    #endregion

    #region Private
    /// <summary>Evaluates the documented mathematical surface as a double-precision reference, not a GPU execution.</summary>
    private static double Displacement(double u, double fill, double seconds, Vector2 motion, int segments = 16, float bob = 0)
    {
        var parameters = LiquidSloshIndicatorEffect.Definition.Parameters;
        double activity = Math.Max(Math.Max(Math.Abs(motion.X), Math.Abs(motion.Y)), Math.Abs(bob));
        double wave = -(parameters.X + parameters.Y * activity)
            * Math.Cos(2 * Math.PI * u) * Math.Cos(2 * Math.PI * seconds / 2 + 0.75 * bob);
        double inverseSegmentsSquared = 1.0 / (segments * segments);
        double edgeMean = 0.2 + 4.0 / 3 * inverseSegmentsSquared
            - 8.0 / 15 * inverseSegmentsSquared * inverseSegmentsSquared;
        double meniscus = parameters.W * (Math.Pow(2 * u - 1, 4) - edgeMean) / (1 - edgeMean);
        double tilt = -parameters.Z * motion.X * (2 * u - 1);
        return Math.Min(0.1, Math.Min(0.25 * fill, 0.25 * (1 - fill))) * (wave + tilt + meniscus);
    }
    #endregion
}
