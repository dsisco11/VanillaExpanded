using System;
using System.Drawing;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Validates and resolves external placement metadata for any measured container.</summary>
internal static class HudContainerAttachment
{
    #region Public API
    /// <summary>Limits persisted attachments to supported external surfaces and finite placement metadata.</summary>
    public static bool IsSupported(HudOverlayPlacement placement) => HudOverlayTargets.IsExternal(placement.TargetId)
        && Enum.IsDefined(placement.Attachment) && Enum.IsDefined(placement.Pivot)
        && double.IsFinite(placement.OffsetX) && double.IsFinite(placement.OffsetY)
        && (placement.ScreenPaddingPixels == null || double.IsFinite(placement.ScreenPaddingPixels.Value) && placement.ScreenPaddingPixels.Value >= 0);

    /// <summary>Resolves a measured container against a physical target using the shared attachment and pivot points.</summary>
    public static PointF Origin(HudOverlayPlacement placement, RectangleF target, SizeF physicalSize, double scale)
    {
        var anchor = HudOverlayPoints.Normalized(placement.Attachment);
        var pivot = HudOverlayPoints.Normalized(placement.Pivot);
        // Registered screen defaults retain their explicit pixel inset or the standard GUI-unit margin.
        if (HudOverlayTargets.Canonical(placement.TargetId) == HudOverlayTargets.Screen)
            target = HudOverlayAnchorContext.Inset(target, placement.ScreenPaddingPixels ?? HudOverlayAnchorContext.SafeInset * scale);
        return new PointF((float)(target.Left + anchor.X * target.Width - pivot.X * physicalSize.Width + placement.OffsetX * scale),
            (float)(target.Top + anchor.Y * target.Height - pivot.Y * physicalSize.Height + placement.OffsetY * scale));
    }

    /// <summary>Applies identical native checkpoint rounding and viewport reachability to previews and live layout.</summary>
    public static PointF Position(HudOverlayPlacement placement, RectangleF target, SizeF physicalSize, RectangleF viewport, double scale)
    {
        var origin = Origin(placement, target, physicalSize, scale);
        var anchor = HudOverlayPoints.Normalized(placement.Attachment);
        var pivot = HudOverlayPoints.Normalized(placement.Pivot);
        double x = (origin.X - viewport.Left) / scale, y = (origin.Y - viewport.Top) / scale;
        // Exterior attachments round away from their target, avoiding fractional overlap with native surfaces.
        x = pivot.X > anchor.X ? Math.Floor(x) : pivot.X < anchor.X ? Math.Ceiling(x) : Math.Round(x, MidpointRounding.AwayFromZero);
        y = pivot.Y > anchor.Y ? Math.Floor(y) : pivot.Y < anchor.Y ? Math.Ceiling(y) : Math.Round(y, MidpointRounding.AwayFromZero);
        return new PointF((int)Math.Clamp(x, 0, Math.Max(0, (viewport.Width - physicalSize.Width) / scale)),
            (int)Math.Clamp(y, 0, Math.Max(0, (viewport.Height - physicalSize.Height) / scale)));
    }

    #endregion
}
