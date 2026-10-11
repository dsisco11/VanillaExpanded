using System;
using VanillaExpanded.HudOverlays.Anchoring;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Resolves persisted placement preferences into validated native attachment metadata.</summary>
internal static class HudOverlayConfiguration
{
    #region Public API
    /// <summary>Returns the stable persisted key for named anchors, screen choices, and ConfigLib numeric events.</summary>
    public static string NormalizeAnchorKey(string? key) => key switch
    {
        "saturation" or "0" => "saturation",
        "hotbar" or "1" => "hotbar",
        "screen-left-top" or "2" => "screen-left-top",
        "screen-center-top" or "3" => "screen-center-top",
        "screen-right-top" or "4" => "screen-right-top",
        "screen-left-middle" or "5" => "screen-left-middle",
        "screen-center-middle" or "6" => "screen-center-middle",
        "screen-right-middle" or "7" => "screen-right-middle",
        "screen-left-bottom" or "8" => "screen-left-bottom",
        "screen-center-bottom" or "9" => "screen-center-bottom",
        "screen-right-bottom" or "10" => "screen-right-bottom",
        _ => "saturation"
    };

    /// <summary>Maps canonical placement keys, falling back independently for invalid offsets.</summary>
    public static HudOverlayPlacement ResolveHeldItemStatusPlacement(VanillaExpandedConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        double x = float.IsFinite(config.HeldItemStatusOffsetX) ? config.HeldItemStatusOffsetX : -4;
        double y = float.IsFinite(config.HeldItemStatusOffsetY) ? config.HeldItemStatusOffsetY : 0;
        // The configuration setter normalizes ConfigLib numeric events to stable persisted keys.
        // Offset fallback is independent per axis so one invalid value does not erase the other preference.
        return config.HeldItemStatusAnchor switch
        {
            "hotbar" => new(HudOverlayAnchorContext.HotbarTargetId,
                HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle, x, y),
            "screen-left-top" => Screen(HudOverlayPoint.LeftTop, x, y),
            "screen-center-top" => Screen(HudOverlayPoint.CenterTop, x, y),
            "screen-right-top" => Screen(HudOverlayPoint.RightTop, x, y),
            "screen-left-middle" => Screen(HudOverlayPoint.LeftMiddle, x, y),
            "screen-center-middle" => Screen(HudOverlayPoint.CenterMiddle, x, y),
            "screen-right-middle" => Screen(HudOverlayPoint.RightMiddle, x, y),
            "screen-left-bottom" => Screen(HudOverlayPoint.LeftBottom, x, y),
            "screen-center-bottom" => Screen(HudOverlayPoint.CenterBottom, x, y),
            "screen-right-bottom" => Screen(HudOverlayPoint.RightBottom, x, y),
            _ => new(HudOverlayAnchorContext.SaturationTargetId,
                HudOverlayPoint.LeftTop, HudOverlayPoint.LeftBottom, x, y)
        };
    }
    #endregion

    #region Private
    /// <summary>Uses matching screen attachment and group pivot so offsets follow native alignment.</summary>
    private static HudOverlayPlacement Screen(HudOverlayPoint point, double x, double y) =>
        new(HudOverlayAnchorContext.ScreenTargetId, point, point, x, y);
    #endregion
}
