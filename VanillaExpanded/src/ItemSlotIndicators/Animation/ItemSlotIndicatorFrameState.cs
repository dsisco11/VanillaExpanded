using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Owns periodic animation time and shared damped camera rates independently of item sampling and slot draws.</summary>
internal sealed class ItemSlotIndicatorFrameState
{
    private double animationSeconds;
    private double previousTime;
    private bool hasTime;
    private ItemSlotIndicatorCameraSample? previousCamera;

    /// <summary>Gets the immutable latest frame inputs; reading never advances animation.</summary>
    internal ItemSlotIndicatorFrameSnapshot Snapshot { get; private set; }

    #region Public API
    /// <summary>Advances once at the GUI boundary using monotonic seconds; simulation pause does not stop GUI animation.</summary>
    /// <param name="monotonicSeconds">Absolute monotonic clock reading, independent of world time and item-render delta time.</param>
    /// <param name="needsCameraMotion">Whether any registered effect requests motion; disabling drops the camera baseline.</param>
    /// <param name="camera">Copied rendered orientation and reset identities, or null when unavailable.</param>
    internal void Update(double monotonicSeconds, bool needsCameraMotion, ItemSlotIndicatorCameraSample? camera)
    {
        double elapsed = hasTime ? monotonicSeconds - previousTime : 0;
        hasTime = double.IsFinite(monotonicSeconds);
        previousTime = monotonicSeconds;
        bool accepted = hasTime && double.IsFinite(elapsed) && elapsed > 0 && elapsed <= 0.25;
        if (accepted) animationSeconds = (animationSeconds + elapsed) % 64;
        // Bound both CPU accumulation and float publication; rounding must never publish the excluded value 64.
        float time = Math.Min((float)animationSeconds, MathF.BitDecrement(64));
        Vector2 motion = Vector2.Zero;
        float bob = 0;
        if (needsCameraMotion && camera is { } current && current.Basis.IsValid())
        {
            if (accepted && previousCamera is { } previous && SameContext(previous, current))
            {
                motion = CalculateMotion(previous.Basis, current.Basis, elapsed, Snapshot.Motion);
                bob = CalculateBob(previous.EyeHeight, current.EyeHeight, elapsed, Snapshot.CameraBob);
            }
            previousCamera = current;
        }
        else previousCamera = null;
        Snapshot = new(time, motion, bob);
    }
    #endregion

    #region Private
    /// <summary>Uses reference identity so replacement objects cannot inherit an old camera impulse.</summary>
    private static bool SameContext(ItemSlotIndicatorCameraSample previous, ItemSlotIndicatorCameraSample current) =>
        ReferenceEquals(previous.World, current.World) && ReferenceEquals(previous.Player, current.Player)
        && ReferenceEquals(previous.Camera, current.Camera) && previous.Mode == current.Mode;

    /// <summary>Normalizes actual local eye-height velocity and damps footstep bob independently of camera rotation.</summary>
    private static float CalculateBob(double? previous, double? current, double elapsed, float priorBob)
    {
        // Missing/disabled bob and large eye-height discontinuities begin neutral, just like a changed camera.
        if (previous is not { } before || current is not { } after
            || !double.IsFinite(before) || !double.IsFinite(after) || Math.Abs(after - before) > 0.25)
            return 0;
        float target = (float)Math.Clamp((after - before) / elapsed / 0.5, -1, 1);
        float weight = (float)(1 - Math.Exp(-elapsed / 0.06));
        return float.Lerp(priorBob, target, weight);
    }

    /// <summary>Computes shortest-arc rotation in previous camera-local axes, then normalizes and exponentially damps rates.</summary>
    private static Vector2 CalculateMotion(ItemSlotIndicatorCameraBasis previous, ItemSlotIndicatorCameraBasis current,
        double elapsed, Vector2 priorMotion)
    {
        // Columns of the relative rotation are the new axes expressed in the previous basis.
        double cosine = Math.Clamp((Vector3.Dot(previous.Right, current.Right) + Vector3.Dot(previous.Up, current.Up)
            + Vector3.Dot(previous.Backward, current.Backward) - 1) / 2.0, -1, 1);
        double angle = Math.Acos(cosine);
        if (angle > Math.PI / 2) return Vector2.Zero;
        double factor = angle < 0.0001 ? 0.5 : angle / (2 * Math.Sin(angle));
        double rightRotation = (Vector3.Dot(previous.Backward, current.Up) - Vector3.Dot(previous.Up, current.Backward)) * factor;
        double upRotation = (Vector3.Dot(previous.Right, current.Backward) - Vector3.Dot(previous.Backward, current.Right)) * factor;
        // Forward is -backward: negative rotation about up looks right, positive rotation about right looks up.
        var target = new Vector2((float)Math.Clamp(-upRotation / elapsed / 4, -1, 1),
            (float)Math.Clamp(rightRotation / elapsed / 4, -1, 1));
        float weight = (float)(1 - Math.Exp(-elapsed / 0.12));
        return Vector2.Lerp(priorMotion, target, weight);
    }
    #endregion
}
