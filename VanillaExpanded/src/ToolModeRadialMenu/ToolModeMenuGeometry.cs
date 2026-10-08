using System;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Applies tool-mode geometry settings consistently across ordinary and material-picker rings.</summary>
internal static class ToolModeMenuGeometry
{
    #region Public API
    /// <summary>Scales an authored center radius with bounded, finite configuration.</summary>
    internal static double GetCenterRadius(double defaultRadius) =>
        defaultRadius * GetMultiplier(VanillaExpandedModSystem.Config.ToolModeCenterSize);

    /// <summary>Places a ring outside its inner menu while scaling only its authored thickness.</summary>
    internal static double GetOuterRadius(double innerRadius, double defaultThickness) =>
        innerRadius + defaultThickness * GetMultiplier(VanillaExpandedModSystem.Config.ToolModeRingSize);
    #endregion

    #region Private
    /// <summary>Bounds hand-edited values and preserves default geometry for non-finite settings.</summary>
    private static double GetMultiplier(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.15f, 2.5f) : 1d;
    #endregion
}
