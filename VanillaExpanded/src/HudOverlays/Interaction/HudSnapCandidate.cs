using System.Drawing;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>The incoming item's attachment side relative to its destination.</summary>
internal enum HudSnapSide { Left, Right, Above, Below }

/// <summary>A measured visible destination; a null member denotes a whole-container target.</summary>
internal sealed record HudSnapTarget(string Container, string? Member, RectangleF Bounds, float Gap);

/// <summary>A geometry-only proposal; identity remains stable while the acquisition distance changes.</summary>
internal sealed record HudSnapCandidate(string Identity, HudSnapTarget? Target, HudSnapSide Side,
    HudOverlayCrossAlignment Alignment, PointF Origin, float Distance, RectangleF Guide)
{
    #region Public API
    public bool Grouping => Target != null;
    public bool AlignsX { get; init; }

    /// <summary>Keeps a destination and attachment side stable while allowing its cross-axis alignment to change.</summary>
    public bool RetainsAttachment(HudSnapCandidate? previous) => Grouping && previous?.Grouping == true
        && Target!.Container == previous.Target!.Container && Target.Member == previous.Target.Member && Side == previous.Side;
    #endregion
}
