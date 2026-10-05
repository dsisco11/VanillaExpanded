using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Contains sanitized straight-alpha presentation and full scaled slot bounds for one indicator submission.</summary>
internal readonly record struct ItemSlotIndicatorDrawInput(Vector4 SlotBounds, float Fill, Vector4 Color)
{
    /// <summary>Gets the provider's sanitized resource fraction independently of mapped geometry height.</summary>
    internal float ResourceFill { get; init; }
    /// <summary>Gets the optional draw levels for shader containment and boundary cues.</summary>
    internal ItemSlotIndicatorDrawRange? DrawRange { get; init; }
    /// <summary>Gets optional immutable ingredient colors sampled by the provider.</summary>
    internal ItemSlotIndicatorParticlePalette? ParticlePalette { get; init; }
    /// <summary>Gets whether adjoining style rectangles preserve fractional GUI coordinates rather than legacy truncation.</summary>
    internal bool PreserveFractionalPosition { get; init; }

    #region Public API
    /// <summary>Rejects invalid geometry and invisible results while retaining negative coordinates for inherited clipping.</summary>
    /// <remarks>Outline and bar tracks retain zero resource levels; draw ranges apply only to backgrounds.</remarks>
    internal static bool TryCreate(double x, double y, float size, ItemSlotIndicator indicator, out ItemSlotIndicatorDrawInput input,
        ItemSlotIndicatorRenderingStyle style = ItemSlotIndicatorRenderingStyle.SlotBackground)
    {
        input = default;
        if (!Enum.IsDefined(style) || !double.IsFinite(x) || !double.IsFinite(y) || !float.IsFinite(size) || size <= 0) return false;
        float left = (float)(x - size / 2), top = (float)(y - size / 2);
        if (!float.IsFinite(left) || !float.IsFinite(top)) return false;
        float fill = Sanitize(indicator.Fill);
        if (indicator.DrawRange is { IsValid: false }) return false;
        bool background = style == ItemSlotIndicatorRenderingStyle.SlotBackground;
        float drawFill = background && indicator.DrawRange is { } range ? float.Lerp(range.Minimum, range.Maximum, fill) : fill;
        Vector4 color = new(Sanitize(indicator.Color.X), Sanitize(indicator.Color.Y), Sanitize(indicator.Color.Z), Sanitize(indicator.Color.W));
        if ((background && drawFill == 0 && indicator.DrawRange is null) || color.W == 0) return false;
        input = new(new(left, top, size, size), drawFill, color)
        {
            ResourceFill = fill, DrawRange = background ? indicator.DrawRange : null, ParticlePalette = indicator.ParticlePalette
        };
        return true;
    }

    /// <summary>Builds a light one-scaled-pixel boundary bar, fading in over the nearest fifteen percent of resource fill.</summary>
    internal bool TryCreateBoundaryCue(out ItemSlotIndicatorDrawInput cue)
    {
        cue = default;
        if (DrawRange is not { } range) return false;
        bool lower = ResourceFill <= 0.5f;
        float distance = lower ? ResourceFill : 1 - ResourceFill;
        if (distance >= 0.15f) return false;
        // Smoothstep removes an abrupt appearance at the approach threshold; the level itself stays fixed.
        float proximity = 1 - distance / 0.15f;
        float alpha = Color.W * 0.6f * proximity * proximity * (3 - 2 * proximity);
        float thickness = SlotBounds.W / 48;
        float y = SlotBounds.Y + SlotBounds.W * (1 - (lower ? range.Minimum : range.Maximum));
        y = Math.Clamp(y - thickness / 2, SlotBounds.Y, SlotBounds.Y + SlotBounds.W - thickness);
        var light = Vector4.Lerp(Color, Vector4.One, 0.65f);
        light.W = alpha;
        cue = new(new(SlotBounds.X, y, SlotBounds.Z, thickness), 1, light);
        return true;
    }

    /// <summary>Accepts finite, complete GUI matrices without reconstructing transforms or inheriting engine array storage.</summary>
    internal static bool ValidMatrix(float[]? matrix)
    {
        if (matrix is null || matrix.Length != 16) return false;
        foreach (float value in matrix) if (!float.IsFinite(value)) return false;
        return true;
    }

    /// <summary>Composes the GUI rectangle transform in owned storage with inherited dialog depth and optional legacy truncation.</summary>
    internal void RectangleMatrix(float[] modelView, float[] destination)
    {
        float width = SlotBounds.Z, height = SlotBounds.W * Fill;
        float x = SlotBounds.X, y = SlotBounds.Y + SlotBounds.W - height;
        if (!PreserveFractionalPosition) { x = (int)x; y = (int)y; }
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
