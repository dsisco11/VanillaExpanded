using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Provides allocation-free rectangle bounds for indicator presentations in scaled GUI coordinates.</summary>
internal readonly record struct ItemSlotIndicatorStyleLayout(
    Vector4 Background, Vector4 OutlineTop, Vector4 OutlineBottom, Vector4 OutlineLeft, Vector4 OutlineRight,
    Vector4 BarTrack, Vector4 BarFill)
{
    #region Public API
    /// <summary>Builds bottom-up background, a complete inset border, and an unmapped left-to-right bar.</summary>
    /// <param name="input">Sanitized slot bounds and sampled resource fraction.</param>
    /// <param name="guiScale">Pixels per unscaled GUI unit, independent of the slot's dimensions.</param>
    internal static ItemSlotIndicatorStyleLayout Create(ItemSlotIndicatorDrawInput input, float guiScale)
    {
        if (!float.IsFinite(guiScale) || guiScale <= 0) throw new ArgumentOutOfRangeException(nameof(guiScale));
        var slot = input.SlotBounds;
        // Clamp decorative dimensions for small slots, preserving nonnegative inner dimensions.
        float inset = Math.Min(2 * guiScale, Math.Min(slot.Z, slot.W) / 4);
        float x = slot.X + inset, y = slot.Y + inset;
        float width = slot.Z - 2 * inset, height = slot.W - 2 * inset;
        float thickness = Math.Min(guiScale, Math.Min(width, height) / 2);
        float barHeight = Math.Min(4 * guiScale, height);
        float backgroundHeight = slot.W * input.Fill;
        // Top/bottom own the corners; side strips only cover the interior between them.
        return new(
            new(slot.X, slot.Y + slot.W - backgroundHeight, slot.Z, backgroundHeight),
            new(x, y, width, thickness), new(x, y + height - thickness, width, thickness),
            new(x, y + thickness, thickness, height - 2 * thickness),
            new(x + width - thickness, y + thickness, thickness, height - 2 * thickness),
            new(x, y + height - barHeight, width, barHeight),
            new(x, y + height - barHeight, width * input.ResourceFill, barHeight));
    }

    /// <summary>Converts explicit bounds into a full rectangle submission for the existing prepared mesh and GUI shader.</summary>
    /// <remarks>Callers skip zero-area bounds. Color selection, track tint, and draw ordering belong to the renderer.</remarks>
    internal static ItemSlotIndicatorDrawInput Rectangle(Vector4 bounds, Vector4 color) =>
        new(bounds, 1, color) { PreserveFractionalPosition = true };
    #endregion
}
