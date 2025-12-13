using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

using Vintagestory.API.MathTools;

namespace VanillaExpanded;
public static class VecExtensions
{
    #region To System Numerics Type Extensions

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector2 ToSNT(this Vec2f vec)
    {
        return new System.Numerics.Vector2([vec.X, vec.Y]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector3 ToSNT(this Vec3f vec)
    {
        return new System.Numerics.Vector3([vec.X, vec.Y, vec.Z]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static System.Numerics.Vector3 ToSNT(this Vec3d vec)
    {
        return new System.Numerics.Vector3([(float)vec.X, (float)vec.Y, (float)vec.Z]);
    }
    #endregion

    #region To "FastVec" casts

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastVec2i ToSlowVeci(this System.Numerics.Vector2 vec)
    {
        return new FastVec2i((int)vec.X, (int)vec.Y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastVec3i ToSlowVeci(this System.Numerics.Vector3 vec)
    {
        //System.Numerics.Vector<float> vecf = vec.AsVector128().AsVector();
        //System.Numerics.Vector<int> veci = System.Numerics.Vector.ConvertToInt32(vecf);
        return new FastVec3i((int)vec.X, (int)vec.Y, (int)vec.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static FastVec3f ToSlowVecf(this System.Numerics.Vector3 vec)
    {
        return Unsafe.As<System.Numerics.Vector3, FastVec3f>(ref vec);
    }
    #endregion

    #region System.Numerics.Vector Compatibility
    public static Vec3f Set(this Vec3f vec, in System.Numerics.Vector3 other)
    {
        vec.X = other.X;
        vec.Y = other.Y;
        vec.Z = other.Z;
        return vec;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec4f Set(this Vec4f vec, in System.Numerics.Vector4 other)
    {
        vec.X = other.X;
        vec.Y = other.Y;
        vec.Z = other.Z;
        vec.W = other.W;
        return vec;
    }
    #endregion
}
