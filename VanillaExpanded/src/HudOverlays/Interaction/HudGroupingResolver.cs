using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Builds immutable insertion trees, preserving both sides' existing attachment relationships.</summary>
internal static class HudGroupingResolver
{
    #region Public API
    /// <summary>Replaces a destination leaf for individual insertion, or wraps the complete destination for group merging.</summary>
    public static HudLayoutNode Insert(HudLayoutNode destination, HudLayoutNode incoming, string? member,
        HudSnapSide side, HudOverlayCrossAlignment alignment, double gap)
    {
        if (member != null && destination.Indicator != member)
        {
            if (destination.Indicator != null) return destination;
            // Rebuild only the path to the selected leaf; untouched branches keep their authored policy and identity.
            return destination with { First = Insert(destination.First!, incoming, member, side, alignment, gap),
                Second = Insert(destination.Second!, incoming, member, side, alignment, gap) };
        }
        bool before = side is HudSnapSide.Left or HudSnapSide.Above;
        return new HudLayoutNode { First = before ? incoming : destination, Second = before ? destination : incoming,
            Direction = side is HudSnapSide.Left or HudSnapSide.Right ? HudOverlayDirection.Horizontal : HudOverlayDirection.Vertical,
            Alignment = alignment, Gap = gap };
    }
    #endregion
}
