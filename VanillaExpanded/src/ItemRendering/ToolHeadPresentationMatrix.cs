using System;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ItemRendering;

/// <summary>Builds screen presentation from owned engine transform values and effective menu placement.</summary>
internal static class ToolHeadPresentationMatrix
{
    #region Public API
    /// <summary>Composes the documented model and screen transforms, rejecting overflow at the draw boundary.</summary>
    internal static bool TryCreate(ToolHeadPresentationProperties properties, double x, double y,
        float sizePixels, double wedgeDegrees, out float[] matrix)
    {
        var transform = properties.CreateModelTransform();
        double angle = properties.WedgeRotationDegrees.HasValue
            ? wedgeDegrees + properties.WedgeRotationDegrees.Value : 0;
        // Size already includes GUI/menu scaling and hover; the negative Y maps model-up to screen-up.
        matrix = Mat4f.Create();
        Mat4f.Translate(matrix, matrix, (float)x, (float)y, 100);
        Mat4f.RotateZ(matrix, matrix, (float)(angle * Math.PI / 180));
        Mat4f.Scale(matrix, matrix, sizePixels, -sizePixels, sizePixels);
        Mat4f.Translate(matrix, matrix, -transform.Origin.X, -transform.Origin.Y, -transform.Origin.Z);
        Mat4f.Mul(matrix, matrix, transform.AsMatrix);
        foreach (float component in matrix)
            if (!float.IsFinite(component)) return false;
        return sizePixels > 0;
    }
    #endregion
}
