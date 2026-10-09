using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Copies camera-local right, up, and backward axes in world space without retaining engine matrix storage.</summary>
internal readonly record struct ItemSlotIndicatorCameraBasis(Vector3 Right, Vector3 Up, Vector3 Backward)
{
    #region Public API
    /// <summary>Copies the rows of a column-major world-to-camera matrix, ignoring translation.</summary>
    internal static ItemSlotIndicatorCameraBasis FromViewMatrix(ReadOnlySpan<double> matrix) => matrix.Length < 16
        ? default : new(new((float)matrix[0], (float)matrix[4], (float)matrix[8]),
            new((float)matrix[1], (float)matrix[5], (float)matrix[9]),
            new((float)matrix[2], (float)matrix[6], (float)matrix[10]));

    /// <summary>Accepts only finite, unit, orthogonal, right-handed bases within the shared 0.001 tolerance.</summary>
    internal bool IsValid()
    {
        const float tolerance = 0.001f;
        // Length comparisons also reject NaN/infinity, including overflow from malformed engine inputs.
        return MathF.Abs(Right.Length() - 1) <= tolerance && MathF.Abs(Up.Length() - 1) <= tolerance
            && MathF.Abs(Backward.Length() - 1) <= tolerance
            && MathF.Abs(Vector3.Dot(Right, Up)) <= tolerance
            && MathF.Abs(Vector3.Dot(Right, Backward)) <= tolerance
            && MathF.Abs(Vector3.Dot(Up, Backward)) <= tolerance
            && MathF.Abs(Vector3.Dot(Vector3.Cross(Right, Up), Backward) - 1) <= tolerance;
    }
    #endregion
}
