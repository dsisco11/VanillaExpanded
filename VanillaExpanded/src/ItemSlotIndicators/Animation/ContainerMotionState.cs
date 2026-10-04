using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Tracks reusable camera-attached container velocity and acceleration in world and local coordinates.</summary>
internal sealed class ContainerMotionState
{
    // A camera-local point 0.8 blocks ahead and 0.2 below the eye converts rotation into translation.
    // Differencing its trajectory includes tangential and centripetal acceleration, including camera roll.
    private static readonly Vector3 containerOffset = new(0, -0.2f, -0.8f);
    private ItemSlotIndicatorCameraSample? previous;
    private Vector3 velocity;
    private Vector3 acceleration;
    private bool hasVelocity;

    /// <summary>Gets whether a valid camera pose supplies the current motion baseline.</summary>
    internal bool HasPose => previous is not null;
    /// <summary>Gets filtered world velocity in blocks per second.</summary>
    internal Vector3 WorldVelocity => velocity;
    /// <summary>Gets filtered world acceleration in blocks per second squared.</summary>
    internal Vector3 WorldAcceleration => acceleration;
    /// <summary>Gets filtered velocity projected onto container right/up/backward axes.</summary>
    internal Vector3 LocalVelocity { get; private set; }
    /// <summary>Gets filtered acceleration projected onto container right/up/backward axes.</summary>
    internal Vector3 LocalAcceleration { get; private set; }

    #region Public API
    /// <summary>Clears all differentiation history so replacement cameras cannot inherit an impulse.</summary>
    internal void Reset()
    {
        previous = null;
        velocity = acceleration = Vector3.Zero;
        LocalVelocity = LocalAcceleration = Vector3.Zero;
        hasVelocity = false;
    }

    /// <summary>Updates container kinematics and reports discontinuities requiring dependent effects to reset.</summary>
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
        // Establish velocity once; entering a moving camera must not look like an abrupt acceleration from zero.
        if (!hasVelocity)
        {
            velocity = targetVelocity;
            LocalVelocity = Project(velocity, current.Basis);
            hasVelocity = true;
            return false;
        }
        var nextVelocity = Vector3.Lerp(velocity, targetVelocity, (float)(1 - Math.Exp(-elapsed / 0.025)));
        var targetAcceleration = (nextVelocity - velocity) / dt;
        velocity = nextVelocity;
        // Keep both histories in world space: rotating local axes must not manufacture acceleration.
        // Previous velocity supplies the braking context; dependent effects own their response and longer-lived inertia.
        acceleration = Vector3.Lerp(acceleration, targetAcceleration, (float)(1 - Math.Exp(-elapsed / 0.045)));
        LocalVelocity = Project(velocity, current.Basis);
        LocalAcceleration = Project(acceleration, current.Basis);
        return false;
    }
    #endregion

    #region Private
    /// <summary>Transforms the conceptual container's fixed camera-local offset into world coordinates.</summary>
    private static Vector3 WorldOffset(ItemSlotIndicatorCameraBasis basis) =>
        basis.Right * containerOffset.X + basis.Up * containerOffset.Y + basis.Backward * containerOffset.Z;

    /// <summary>Expresses a world vector in the current container axes without altering derivative history.</summary>
    private static Vector3 Project(Vector3 value, ItemSlotIndicatorCameraBasis basis) =>
        new(Vector3.Dot(value, basis.Right), Vector3.Dot(value, basis.Up), Vector3.Dot(value, basis.Backward));

    /// <summary>Tests reference identities and camera mode rather than mutable position values.</summary>
    private static bool SameContext(ItemSlotIndicatorCameraSample a, ItemSlotIndicatorCameraSample b) =>
        ReferenceEquals(a.World, b.World) && ReferenceEquals(a.Player, b.Player)
        && ReferenceEquals(a.Camera, b.Camera) && a.Mode == b.Mode;
    #endregion
}
