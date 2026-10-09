using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks time-based jostle continuity, limits, and reset reproducibility without graphics allocation.</summary>
[Trait("Category", "Unit")]
public sealed class LiquidSloshJostleNoiseTests
{
    #region Public API
    /// <summary>Different frame subdivisions reach the same noise sample at the same elapsed time.</summary>
    [Fact]
    public void Sampling_IsIndependentOfFrameRate()
    {
        var fast = new LiquidSloshJostleNoise();
        var slow = new LiquidSloshJostleNoise();
        for (int frame = 0; frame < 240; frame++)
        {
            fast.Advance(1.0 / 120);
            float sample = fast.Advance(1.0 / 120);
            Assert.InRange(MathF.Abs(sample - slow.Advance(1.0 / 60)), 0, 0.00001f);
        }
    }

    /// <summary>Noise remains bounded and continuous across knot transitions while varying in both directions.</summary>
    [Fact]
    public void Samples_AreSmoothBoundedAndSigned()
    {
        var noise = new LiquidSloshJostleNoise();
        float previous = noise.Advance(0.001), minimum = previous, maximum = previous;
        for (int sample = 0; sample < 10_000; sample++)
        {
            float next = noise.Advance(0.001);
            Assert.InRange(next, -1, 1);
            Assert.InRange(MathF.Abs(next - previous), 0, 0.02f);
            minimum = MathF.Min(minimum, next);
            maximum = MathF.Max(maximum, next);
            previous = next;
        }
        Assert.True(minimum < -0.1f && maximum > 0.1f);
    }

    /// <summary>A discontinuity resets both sequence phase and knot history.</summary>
    [Fact]
    public void Reset_RestoresOriginalSequence()
    {
        var noise = new LiquidSloshJostleNoise();
        float first = noise.Advance(0.02);
        for (int sample = 0; sample < 100; sample++) noise.Advance(0.02);
        noise.Reset();
        Assert.Equal(first, noise.Advance(0.02));
    }
    #endregion
}
