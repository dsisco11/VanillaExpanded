using System;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Immutable target attachment metadata; it neither resolves targets nor calculates native bounds.</summary>
public sealed record HudOverlayPlacement
{
    public string TargetId { get; }
    public HudOverlayPoint Attachment { get; }
    public HudOverlayPoint Pivot { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    /// <summary>Optional screen-edge inset in physical pixels, independent of GUI scale.</summary>
    public double? ScreenPaddingPixels { get; }
    #region Public API
    /// <summary>Defines one named/screen target attachment using finite unscaled GUI-unit offsets.</summary>
    public HudOverlayPlacement(string targetId, HudOverlayPoint attachment, HudOverlayPoint pivot, double offsetX = 0, double offsetY = 0, double? screenPaddingPixels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (!Enum.IsDefined(attachment)) throw new ArgumentOutOfRangeException(nameof(attachment));
        if (!Enum.IsDefined(pivot)) throw new ArgumentOutOfRangeException(nameof(pivot));
        if (!double.IsFinite(offsetX)) throw new ArgumentOutOfRangeException(nameof(offsetX));
        if (!double.IsFinite(offsetY)) throw new ArgumentOutOfRangeException(nameof(offsetY));
        if (screenPaddingPixels is double padding && (!double.IsFinite(padding) || padding < 0))
            throw new ArgumentOutOfRangeException(nameof(screenPaddingPixels));
        ScreenPaddingPixels = screenPaddingPixels;
        TargetId = targetId;
        Attachment = attachment;
        Pivot = pivot;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
    #endregion
}
