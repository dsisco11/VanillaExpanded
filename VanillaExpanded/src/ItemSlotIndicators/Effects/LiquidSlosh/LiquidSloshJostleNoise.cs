using System;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Produces deterministic smooth, signed noise using elapsed time rather than frame-count sampling.</summary>
internal sealed class LiquidSloshJostleNoise
{
    private const double IntervalSeconds = 0.35;
    private double time;
    private uint segment;

    #region Public API
    /// <summary>Restarts the sequence on camera or simulation discontinuities.</summary>
    internal void Reset()
    {
        time = 0;
        segment = 0;
    }

    /// <summary>Advances accepted elapsed time and returns a smooth value bounded by minus one and one.</summary>
    internal float Advance(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0 || elapsed > 0.25)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        time += elapsed;
        while (time >= IntervalSeconds)
        {
            time -= IntervalSeconds;
            segment = unchecked(segment + 1);
        }
        float t = (float)(time / IntervalSeconds);
        // Quintic interpolation has zero first and second derivatives at both knot boundaries.
        float weight = t * t * t * (t * (6 * t - 15) + 10);
        return float.Lerp(Knot(segment), Knot(unchecked(segment + 1)), weight);
    }
    #endregion

    #region Private
    /// <summary>Hashes each knot into a signed value with no preferred lateral direction or short repeating cycle.</summary>
    private static float Knot(uint index)
    {
        uint value = unchecked(index + 0x9e3779b9u);
        value = unchecked((value ^ (value >> 16)) * 0x7feb352du);
        value = unchecked((value ^ (value >> 15)) * 0x846ca68bu);
        value ^= value >> 16;
        return (value >> 8) / 8388607.5f - 1;
    }
    #endregion
}
