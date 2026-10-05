using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Defines shared straight-alpha indicator colors and ordered palettes independently of feature opacity.</summary>
internal readonly struct IndicatorColorPallette
{
    #region Color Definitions
    /// <summary>Shared red for stale states and empty-resource warnings.</summary>
    internal static readonly Vector4 Red = new(0.88f, 0.08f, 0.05f, 1);
    /// <summary>Shared orange for low or declining states.</summary>
    internal static readonly Vector4 Orange = new(0.95f, 0.38f, 0.05f, 1);
    /// <summary>Shared yellow for intermediate states.</summary>
    internal static readonly Vector4 Yellow = new(0.88f, 0.88f, 0.08f, 1);
    /// <summary>Shared green for fresh or healthy states.</summary>
    internal static readonly Vector4 Green = new(0.18f, 0.48f, 0.24f, 1);
    /// <summary>Shared dark blue for nearly depleted water.</summary>
    internal static readonly Vector4 DarkBlue = new(0.01f, 0.035f, 0.12f, 1);
    /// <summary>Shared light blue for full water resources.</summary>
    internal static readonly Vector4 LightBlue = new(0.14f, 0.48f, 0.88f, 1);
    /// <summary>Muted slate for preparation that has just begun.</summary>
    internal static readonly Vector4 Slate = new(0.26f, 0.32f, 0.40f, 1);
    /// <summary>Bright teal for preparation approaching completion.</summary>
    internal static readonly Vector4 Teal = new(0.12f, 0.72f, 0.60f, 1);
    #endregion

    #region Gradient Definitions
    /// <summary>Freshness colors ordered from stale to fresh.</summary>
    internal static readonly ImmutableArray<Vector4> FreshnessColors = [Red, Orange, Yellow, Green];
    /// <summary>Water colors ordered from nearly empty to full.</summary>
    internal static readonly ImmutableArray<Vector4> WaterColors = [LightBlue, LightBlue];
    /// <summary>Preparation colors ordered from newly started to ready.</summary>
    internal static readonly ImmutableArray<Vector4> PreparationColors = [Slate, Teal];
    #endregion

    #region Public API
    /// <summary>Applies a feature-specific opacity without changing the shared RGB definition.</summary>
    internal static Vector4 WithOpacity(Vector4 color, float opacity)
    {
        color.W = Math.Clamp(opacity, 0, 1);
        return color;
    }
    #endregion
}
