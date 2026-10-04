using System;
using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Animation;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Derives unified container acceleration from a camera-attached point and its filtered world velocity history.</summary>
internal sealed class LiquidSloshMotionState
{
    // A camera-local point 0.8 blocks ahead and 0.2 below the eye converts rotation into translation.
    // Differencing its trajectory includes tangential and centripetal acceleration, including camera roll.
    private static readonly Vector3 containerOffset = new(0, -0.2f, -0.8f);
    private const float SolverAccelerationScale = 0.2f;
    private readonly LiquidSloshJostleNoise jostle = new();
    private ItemSlotIndicatorCameraSample? previous;
    private Vector3 velocity;
    private Vector3 acceleration;
    private bool hasVelocity;

    /// <summary>Gets the unified acceleration projected onto container right/up axes in solver units.</summary>
    internal Vector2 ContainerAcceleration { get; private set; }

    /// <summary>Gets the shared signed variation used to distribute vertical forcing across broad surface modes.</summary>
    internal float VerticalShapeVariation { get; private set; }

    #region Public API
    /// <summary>Clears all differentiation history so replacement cameras cannot inherit an impulse.</summary>
    internal void Reset()
    {
        previous = null;
        velocity = acceleration = Vector3.Zero;
        ContainerAcceleration = Vector2.Zero;
        VerticalShapeVariation = 0;
        hasVelocity = false;
        jostle.Reset();
    }

    /// <summary>Updates shared forces and reports discontinuities requiring a flat GPU state reset.</summary>
    internal bool Update(double elapsed, ItemSlotIndicatorCameraSample? sample)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0 || elapsed > 0.25 || sample is not { } current
            || !current.Basis.IsValid() || current.Position is not { IsValid: true } position)
        {
            bool changed = previous is not null;
            Reset();
            return changed;
        }
        if (previous is not { } before || !SameContext(before, current))
        {
            Reset();
            previous = current;
            return true;
        }
        previous = current;
        var oldPosition = before.Position!.Value;
        // Subtract doubles before converting deltas to floats, preserving motion far from world origin.
        var delta = new Vector3((float)(position.X - oldPosition.X), (float)(position.Y - oldPosition.Y),
            (float)(position.Z - oldPosition.Z));
        float cosine = (Vector3.Dot(before.Basis.Right, current.Basis.Right)
            + Vector3.Dot(before.Basis.Up, current.Basis.Up)
            + Vector3.Dot(before.Basis.Backward, current.Basis.Backward) - 1) * 0.5f;
        if (delta.LengthSquared() > 4 || cosine < 0)
        {
            Reset();
            previous = current;
            return true;
        }
        float dt = (float)elapsed;
        // Difference the conceptual point's world trajectory, rather than adding meters to rotation angles.
        // Offsets remain small floats; the camera origin delta was already subtracted in double precision.
        var targetVelocity = (delta + WorldOffset(current.Basis) - WorldOffset(before.Basis)) / dt;
        float noise = jostle.Advance(elapsed);
        VerticalShapeVariation = noise;
        // Establish velocity once; entering a moving camera must not look like an abrupt acceleration from zero.
        if (!hasVelocity)
        {
            velocity = targetVelocity;
            hasVelocity = true;
            return false;
        }
        var nextVelocity = Vector3.Lerp(velocity, targetVelocity, (float)(1 - Math.Exp(-elapsed / 0.025)));
        var targetAcceleration = (nextVelocity - velocity) / dt;
        velocity = nextVelocity;
        // Keep both histories in world space: rotating local axes must not manufacture acceleration.
        // Previous velocity supplies the braking context; the GPU grid supplies the longer fluid inertia.
        acceleration = Vector3.Lerp(acceleration, targetAcceleration, (float)(1 - Math.Exp(-elapsed / 0.045)));
        var projected = new Vector2(Vector3.Dot(acceleration, current.Basis.Right),
            Vector3.Dot(acceleration, current.Basis.Up)) * SolverAccelerationScale;
        // Slightly imperfect handling couples vertical shaking into lateral forcing. Keep this
        // artistic perturbation out of the differentiated history, and let vertical settling fade it.
        projected.X += noise * MathF.Abs(projected.Y) * 0.08f;
        // Limit magnitude uniformly, preserving the combined direction rather than clipping each axis.
        ContainerAcceleration = projected / MathF.Max(1, projected.Length() / 3);
        return false;
    }
    #endregion

    #region Private
    /// <summary>Transforms the conceptual container's fixed camera-local offset into world coordinates.</summary>
    private static Vector3 WorldOffset(ItemSlotIndicatorCameraBasis basis) =>
        basis.Right * containerOffset.X + basis.Up * containerOffset.Y + basis.Backward * containerOffset.Z;

    /// <summary>Tests reference identities and camera mode rather than mutable position values.</summary>
    private static bool SameContext(ItemSlotIndicatorCameraSample a, ItemSlotIndicatorCameraSample b) =>
        ReferenceEquals(a.World, b.World) && ReferenceEquals(a.Player, b.Player)
        && ReferenceEquals(a.Camera, b.Camera) && a.Mode == b.Mode;
    #endregion
}
