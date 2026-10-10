using System;

namespace VanillaExpanded.HudOverlays.Layout;

/// <summary>Immutable group packing defaults; padding contributes to content size rather than native root padding.</summary>
internal sealed record HudOverlayPacking
{
    public HudOverlayDirection Direction { get; }
    public HudOverlayCrossAlignment CrossAlignment { get; }
    public double Gap { get; }
    public double Padding { get; }
    #region Public API
    /// <summary>Creates packing metadata with a six-unit gap and four-unit padding on every side by default.</summary>
    public HudOverlayPacking(HudOverlayDirection direction = HudOverlayDirection.Vertical, double gap = 6, double padding = 4,
        HudOverlayCrossAlignment crossAlignment = HudOverlayCrossAlignment.Start)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!Enum.IsDefined(crossAlignment)) throw new ArgumentOutOfRangeException(nameof(crossAlignment));
        if (!double.IsFinite(gap) || gap < 0) throw new ArgumentOutOfRangeException(nameof(gap));
        if (!double.IsFinite(padding) || padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
        Direction = direction;
        Gap = gap;
        Padding = padding;
        CrossAlignment = crossAlignment;
    }
    #endregion
}
