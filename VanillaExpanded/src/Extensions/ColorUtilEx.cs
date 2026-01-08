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
}
