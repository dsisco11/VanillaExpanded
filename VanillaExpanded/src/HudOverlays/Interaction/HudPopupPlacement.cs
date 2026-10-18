using System;
using System.Drawing;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Fits physical popup extents to the available viewport independently of anchor-row offsets.</summary>
internal static class HudPopupPlacement
{
    #region Public API
    /// <summary>Clamps the visible popup, reducing its viewport when scrolling or clipping is necessary.</summary>
    public static RectangleF Fit(PointF pointer, SizeF size, RectangleF viewport)
    {
        if (!float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y) || !float.IsFinite(size.Width)
            || !float.IsFinite(size.Height) || !float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y)
            || !float.IsFinite(viewport.Width) || !float.IsFinite(viewport.Height)
            || size.Width <= 0 || size.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return RectangleF.Empty;
        float width = Math.Min(size.Width, viewport.Width), height = Math.Min(size.Height, viewport.Height);
        return new(Math.Clamp(pointer.X, viewport.Left, viewport.Right - width),
            Math.Clamp(pointer.Y, viewport.Top, viewport.Bottom - height), width, height);
    }
    #endregion
}
