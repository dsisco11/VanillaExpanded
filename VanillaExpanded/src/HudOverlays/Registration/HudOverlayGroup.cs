using System;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>One stable group identity with exactly one immutable placement and packing definition.</summary>
internal sealed record HudOverlayGroup
{
    public string Id { get; }
    public HudOverlayPlacement Placement { get; }
    public HudOverlayPacking Packing { get; }
    #region Public API
    /// <summary>Creates group metadata without resolving an engine target or allocating GUI resources.</summary>
    public HudOverlayGroup(string id, HudOverlayPlacement placement, HudOverlayPacking? packing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(placement);
        Id = id;
        Placement = placement;
        Packing = packing ?? new HudOverlayPacking();
    }
    #endregion
}
