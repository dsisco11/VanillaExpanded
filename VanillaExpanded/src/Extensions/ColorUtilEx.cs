using System;
using System.Numerics;

using Vintagestory.API.MathTools;

namespace VanillaExpanded;

/// <summary>
/// Extended color utilities complementing <see cref="ColorUtil"/>.
/// </summary>
public static class ColorUtilEx
{
    /// <summary>
    /// Transparent white as RGBA floats (0..1). RGB=1, Alpha=0.
    /// </summary>
    public static readonly float[] TransparentWhiteRgbaFloat = [1f, 1f, 1f, 0f];

    /// <summary>
    /// Transparent white as RGBA Vec4f (0..1). RGB=1, Alpha=0.
    /// </summary>
    public static readonly Vec4f TransparentWhiteRgbaVec = new(1f, 1f, 1f, 0f);

    /// <summary>
    /// Transparent white as RGBA doubles (0..1). RGB=1, Alpha=0.
    /// </summary>
    public static readonly double[] TransparentWhiteRgbaDouble = [1.0, 1.0, 1.0, 0.0];

    /// <summary>
    /// Transparent white as RGBA bytes (0..255). RGB=255, Alpha=0.
    /// </summary>
    public static readonly byte[] TransparentWhiteRgbaBytes = [255, 255, 255, 0];

    /// <summary>
    /// Transparent black as RGBA floats (0..1). RGB=0, Alpha=0.
    /// </summary>
    public static readonly float[] TransparentBlackRgbaFloat = [0f, 0f, 0f, 0f];

    /// <summary>
    /// Transparent black as RGBA Vec4f (0..1). RGB=0, Alpha=0.
    /// </summary>
    public static readonly Vec4f TransparentBlackRgbaVec = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// Transparent black as RGBA doubles (0..1). RGB=0, Alpha=0.
    /// </summary>
    public static readonly double[] TransparentBlackRgbaDouble = [0.0, 0.0, 0.0, 0.0];

    /// <summary>
    /// Transparent black as RGBA bytes (0..255). RGB=0, Alpha=0.
    /// </summary>
    public static readonly byte[] TransparentBlackRgbaBytes = [0, 0, 0, 0];

    #region Public API
    /// <summary>Interpolates straight-alpha RGBA colors at evenly spaced stops across a clamped 0..1 amount.</summary>
    /// <param name="colors">At least one color, ordered from amount zero to amount one.</param>
    /// <param name="amount">Interpolation amount; NaN selects the first color.</param>
    /// <param name="smooth">Whether to apply smoothstep easing within each color segment.</param>
    /// <returns>The interpolated RGB and alpha channels, without premultiplication.</returns>
    /// <exception cref="ArgumentException">The color palette is empty.</exception>
    public static Vector4 MultiLerp(ReadOnlySpan<Vector4> colors, float amount, bool smooth = false)
    {
        if (colors.IsEmpty) throw new ArgumentException("At least one color is required.", nameof(colors));
        amount = Math.Clamp(float.IsNaN(amount) ? 0 : amount, 0, 1);
        if (colors.Length == 1 || amount <= 0) return colors[0];
        if (amount >= 1) return colors[^1];

        float position = amount * (colors.Length - 1);
        int segment = Math.Min((int)position, colors.Length - 2);
        float blend = position - segment;
        if (smooth) blend = blend * blend * (3 - 2 * blend);
        return Vector4.Lerp(colors[segment], colors[segment + 1], blend);
    }
    #endregion
}
