using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Vintagestory.API.MathTools;

namespace VanillaExpanded;
public static class FastVecExtensions
{
    #region AsSpan Extensions
    // Since the FastVec structs are blittable, we can create spans over their fields for standardized access.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<int> AsSpan(this FastVec2i vec)
    {
        return MemoryMarshal.CreateSpan(ref Unsafe.As<FastVec2i, int>(ref vec), 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<float> AsSpan(this FastVec3f vec)
    {
        return MemoryMarshal.CreateSpan(ref Unsafe.As<FastVec3f, float>(ref vec), 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<double> AsSpan(this FastVec3d vec)
    {
        return MemoryMarshal.CreateSpan(ref Unsafe.As<FastVec3d, double>(ref vec), 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<int> AsSpan(this FastVec3i vec)
    {
        return MemoryMarshal.CreateSpan(ref Unsafe.As<FastVec3i, int>(ref vec), 3);
    }
    #endregion

    #region To System Numerics Type Extensions
    // These extensions remap the memory of FastVec structs into System.Numerics types for better performance.

    /// <summary>
    /// Remaps the memory of a FastVec3f into a System.Numerics.Vector3.
    /// </summary>
    /// <param name="vec"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector3 ToSNT(this FastVec3f vec)
    {
        return new System.Numerics.Vector3(vec.AsSpan());
    }
    #endregion

}
