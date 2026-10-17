using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.HudOverlays.Layout;

/// <summary>Describes a leaf or ordered pair within one flat movable container.</summary>
internal sealed record HudLayoutNode
{
    public string? Indicator { get; init; }
    public HudOverlayDirection Direction { get; init; }
    public HudOverlayCrossAlignment Alignment { get; init; }
    public double Gap { get; init; }
    public HudLayoutNode? First { get; init; }
    public HudLayoutNode? Second { get; init; }

    #region Public API
    /// <summary>Enumerates structural membership independently of visibility.</summary>
    public IEnumerable<string> Leaves() => Indicator != null ? new[] { Indicator }
        : (First?.Leaves() ?? Array.Empty<string>()).Concat(Second?.Leaves() ?? Array.Empty<string>());

    /// <summary>Removes a member during explicit detachment, pruning only truly empty branches.</summary>
    public HudLayoutNode? Remove(string id)
    {
        if (Indicator != null) return Indicator == id ? null : this;
        var first = First?.Remove(id);
        var second = Second?.Remove(id);
        return first == null ? second : second == null ? first : this with { First = first, Second = second };
    }

    /// <summary>Converts authored packing into an equivalent ordered pair tree.</summary>
    public static HudLayoutNode? FromMembers(IEnumerable<string> ids, HudOverlayPacking packing)
    {
        HudLayoutNode? result = null;
        foreach (string id in ids)
        {
            var leaf = new HudLayoutNode { Indicator = id };
            result = result == null ? leaf : new HudLayoutNode { First = result, Second = leaf,
                Direction = packing.Direction, Alignment = packing.CrossAlignment, Gap = packing.Gap };
        }
        return result;
    }
    #endregion
}
