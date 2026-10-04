using System;
using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Animation;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Converts reusable container kinematics into bounded liquid forcing and smooth jostle.</summary>
internal sealed class LiquidSloshMotionState
{
    private readonly ContainerMotionState container = new();
    private readonly LiquidSloshJostleNoise jostle = new();

    /// <summary>Gets bounded horizontal/vertical acceleration in liquid solver units.</summary>
    internal Vector2 ContainerAcceleration { get; private set; }
    /// <summary>Gets the shared signed variation used to distribute vertical forcing across broad surface modes.</summary>
    internal float VerticalShapeVariation { get; private set; }

    #region Public API
    /// <summary>Clears container derivatives and the liquid response on lifecycle discontinuities.</summary>
    internal void Reset()
    {
        container.Reset();
        ResetResponse();
    }

    /// <summary>Updates generic motion and derives liquid forcing without contaminating its velocity history.</summary>
    internal bool Update(double elapsed, ItemSlotIndicatorCameraSample? sample)
    {
        bool reset = container.Update(elapsed, sample);
        if (reset || !container.HasPose)
        {
            ResetResponse();
            return reset;
        }
        float noise = jostle.Advance(elapsed);
        VerticalShapeVariation = noise;
        var acceleration = container.LocalAcceleration;
        var projected = new Vector2(acceleration.X, acceleration.Y) * 0.2f;
        // Imperfect handling remains liquid-specific; generic motion exposes all three physical axes.
        projected.X += noise * MathF.Abs(projected.Y) * 0.08f;
        ContainerAcceleration = projected / MathF.Max(1, projected.Length() / 3);
        return false;
    }
    #endregion

    #region Private
    /// <summary>Resets liquid-only output and noise when generic container history becomes discontinuous.</summary>
    private void ResetResponse()
    {
        ContainerAcceleration = Vector2.Zero;
        VerticalShapeVariation = 0;
        jostle.Reset();
    }
    #endregion
}