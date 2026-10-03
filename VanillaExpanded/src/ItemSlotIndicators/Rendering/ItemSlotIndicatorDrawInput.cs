using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Contains sanitized straight-alpha presentation and full scaled slot bounds for one indicator submission.</summary>
internal readonly record struct ItemSlotIndicatorDrawInput(Vector4 SlotBounds, float Fill, Vector4 Color)
{
    #region Public API
    /// <summary>Rejects invalid geometry and invisible results while retaining negative coordinates for inherited clipping.</summary>
    internal static bool TryCreate(double x, double y, float size, ItemSlotIndicator indicator, out ItemSlotIndicatorDrawInput input)
    {
        input = default;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !float.IsFinite(size) || size <= 0) return false;
        float left = (float)(x - size / 2), top = (float)(y - size / 2);
        if (!float.IsFinite(left) || !float.IsFinite(top)) return false;
        float fill = Sanitize(indicator.Fill);
        Vector4 color = new(Sanitize(indicator.Color.X), Sanitize(indicator.Color.Y), Sanitize(indicator.Color.Z), Sanitize(indicator.Color.W));
        if (fill == 0 || color.W == 0) return false;
        input = new(new(left, top, size, size), fill, color);
        return true;
    }

    /// <summary>Accepts finite, complete GUI matrices without reconstructing transforms or inheriting engine array storage.</summary>
    internal static bool ValidMatrix(float[]? matrix)
    {
        if (matrix is null || matrix.Length != 16) return false;
        foreach (float value in matrix) if (!float.IsFinite(value)) return false;
        return true;
    }

    /// <summary>Composes the legacy GUI rectangle transform in owned storage with integer truncation and inherited dialog depth.</summary>
    internal void RectangleMatrix(float[] modelView, float[] destination)
    {
        float width = SlotBounds.Z, height = SlotBounds.W * Fill;
        float x = (int)SlotBounds.X, y = (int)(SlotBounds.Y + SlotBounds.W - height);
        // Algebraic composition of M * T(x,y,80) * S(w,h,0) * S(.5,.5,0) * T(1,1,0).
        for (int row = 0; row < 4; row++)
        {
            destination[row] = modelView[row] * width / 2;
            destination[4 + row] = modelView[4 + row] * height / 2;
            destination[8 + row] = 0;
            destination[12 + row] = modelView[12 + row] + modelView[row] * (x + width / 2)
                + modelView[4 + row] * (y + height / 2) + modelView[8 + row] * 80;
        }
    }
    #endregion

    #region Private
    /// <summary>Maps nonfinite presentation components to zero and finite ones into the shared unit interval.</summary>
    private static float Sanitize(float value) => Math.Clamp(float.IsFinite(value) ? value : 0, 0, 1);
    #endregion
}
