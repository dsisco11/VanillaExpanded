using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Maps attachment metadata to native alignment and normalized attachment coordinates.</summary>
internal static class HudOverlayPoints
{
    #region Public API
    /// <summary>Returns the native screen alignment for a validated nine-position point.</summary>
    public static EnumDialogArea Alignment(HudOverlayPoint point) => point switch
    {
        HudOverlayPoint.LeftTop => EnumDialogArea.LeftTop,
        HudOverlayPoint.CenterTop => EnumDialogArea.CenterTop,
        HudOverlayPoint.RightTop => EnumDialogArea.RightTop,
        HudOverlayPoint.LeftMiddle => EnumDialogArea.LeftMiddle,
        HudOverlayPoint.CenterMiddle => EnumDialogArea.CenterMiddle,
        HudOverlayPoint.RightMiddle => EnumDialogArea.RightMiddle,
        HudOverlayPoint.LeftBottom => EnumDialogArea.LeftBottom,
        HudOverlayPoint.CenterBottom => EnumDialogArea.CenterBottom,
        HudOverlayPoint.RightBottom => EnumDialogArea.RightBottom,
        _ => throw new System.ArgumentOutOfRangeException(nameof(point))
    };

    /// <summary>Returns the normalized attachment coordinate used only by attachment geometry.</summary>
    public static PointF Normalized(HudOverlayPoint point)
    {
        if (!System.Enum.IsDefined(point)) throw new System.ArgumentOutOfRangeException(nameof(point));
        return new PointF((int)point % 3 * .5f, (int)point / 3 * .5f);
    }
    #endregion
}
