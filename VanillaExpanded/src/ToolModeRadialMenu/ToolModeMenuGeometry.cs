using System;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Applies tool-mode geometry settings consistently across ordinary and material-picker rings.</summary>
internal static class ToolModeMenuGeometry
{
    #region Layout Defaults
    /// <summary>Base radius scale shared by all tool-mode menus and the material picker.</summary>
    internal const double DefaultRadiusScale = 0.54;
    /// <summary>Normalized thickness shared by every tool-mode option ring.</summary>
    internal const double DefaultRingThickness = 0.3432;
    /// <summary>Default radius of the current-mode center circle.</summary>
    internal const double CenterRadius = 0.432;
    /// <summary>Default radius of the material picker's Back circle.</summary>
    internal const double MaterialCenterRadius = 0.324;
    /// <summary>Angular half-gap in degrees shared by specialized tool-mode rings.</summary>
    internal const double SeparatorDegrees = 1.5;
    /// <summary>Angular half-gap in degrees for the generic tool-mode ring.</summary>
    internal const double GenericSeparatorDegrees = 1.0;
    /// <summary>Preferred font scale for the current-mode center label.</summary>
    internal const double CenterLabelFontScale = 1.25;
    #endregion

    #region Public API
    /// <summary>Scales an authored center radius with bounded, finite configuration.</summary>
    internal static double GetCenterRadius(double defaultRadius) =>
        defaultRadius * GetMultiplier(VanillaExpandedModSystem.Config.ToolModeCenterSize);

    /// <summary>Places a ring outside its inner menu while applying the shared ring thickness and live size setting.</summary>
    internal static double GetOuterRadius(double innerRadius) =>
        innerRadius + DefaultRingThickness * GetMultiplier(VanillaExpandedModSystem.Config.ToolModeRingSize);
    #endregion

    #region Private
    /// <summary>Bounds hand-edited values and preserves default geometry for non-finite settings.</summary>
    private static double GetMultiplier(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.15f, 2.5f) : 1d;
    #endregion
}
