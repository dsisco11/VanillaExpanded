using System;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Immutable target attachment metadata; it neither resolves targets nor calculates native bounds.</summary>
internal sealed record HudOverlayPlacement
{
    public string TargetId { get; }
    public HudOverlayPoint Attachment { get; }
    public HudOverlayPoint Pivot { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    #region Public API
    /// <summary>Defines one named/screen target attachment using finite unscaled GUI-unit offsets.</summary>
    public HudOverlayPlacement(string targetId, HudOverlayPoint attachment, HudOverlayPoint pivot, double offsetX = 0, double offsetY = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (!Enum.IsDefined(attachment)) throw new ArgumentOutOfRangeException(nameof(attachment));
        if (!Enum.IsDefined(pivot)) throw new ArgumentOutOfRangeException(nameof(pivot));
        if (!double.IsFinite(offsetX)) throw new ArgumentOutOfRangeException(nameof(offsetX));
        if (!double.IsFinite(offsetY)) throw new ArgumentOutOfRangeException(nameof(offsetY));
        TargetId = targetId;
        Attachment = attachment;
        Pivot = pivot;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
    #endregion
}
