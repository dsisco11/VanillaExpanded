using System.Drawing;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>The incoming item's attachment side relative to its destination.</summary>
internal enum HudSnapSide { Left, Right, Above, Below }

/// <summary>A measured visible destination; a null member denotes a whole-container target.</summary>
internal sealed record HudSnapTarget(string Container, string? Member, RectangleF Bounds, float Gap);

/// <summary>A borrowed fixed HUD rectangle used for alignment and collision checks, never group membership.</summary>
internal sealed record HudSnapBoundary(string Id, RectangleF Bounds);

/// <summary>A geometry-only proposal; identity remains stable while the acquisition distance changes.</summary>
/// <param name="Identity">Deterministic candidate key used for retention and tie-breaking; includes the selected guide or attachment alignment.</param>
/// <param name="Target">VE destination container or member for grouping; null for alignment-only candidates.</param>
/// <param name="Side">Incoming item's side relative to the target; applies to grouping and exterior native-boundary alignment.</param>
/// <param name="Alignment">Cross-axis placement of incoming content for grouping; unused by alignment-only candidates.</param>
/// <param name="Origin">Proposed incoming top-left position in framebuffer pixels, before final grouping layout reconciliation.</param>
/// <param name="Distance">Acquisition distance in physical pixels: pointer-to-edge distance for grouping, or incoming placement correction for alignment.</param>
/// <param name="Guide">Target bounds for grouping, or alignment-line extents in framebuffer pixels; an alignment line may have zero width or height.</param>
internal sealed record HudSnapCandidate(string Identity, HudSnapTarget? Target, HudSnapSide Side,
    HudOverlayCrossAlignment Alignment, PointF Origin, float Distance, RectangleF Guide)
{
    #region Public API
    /// <summary>Gets whether this candidate changes VE membership rather than only aligning placement.</summary>
    public bool Grouping => Target != null;
    /// <summary>Gets the coordinate constrained by an alignment-only guide; grouping candidates use <see cref="HudSnapAxis.None"/>.</summary>
    public HudSnapAxis Axis { get; init; }
    /// <summary>Gets the start, center, or end guide selected along <see cref="Axis"/>; unused by grouping and exterior alignment.</summary>
    public HudOverlayCrossAlignment GuideAlignment { get; init; }
    /// <summary>Gets the borrowed native HUD alignment surface, or null for screen guides and VE targets; never grants membership or ownership.</summary>
    public HudSnapBoundary? Boundary { get; init; }
    /// <summary>Gets whether this alignment places incoming content outside a native boundary on <see cref="Side"/>, requiring rounding away from that surface.</summary>
    public bool Exterior { get; init; }

    /// <summary>Keeps a destination and attachment side stable while allowing its cross-axis alignment to change.</summary>
    public bool RetainsAttachment(HudSnapCandidate? previous) => Grouping && previous?.Grouping == true
        && Target!.Container == previous.Target!.Container && Target.Member == previous.Target.Member && Side == previous.Side;
    #endregion
}
