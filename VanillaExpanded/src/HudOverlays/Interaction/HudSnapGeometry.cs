using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Calculates physical-pixel candidates without changing membership or retaining pointer corrections.</summary>
internal static class HudSnapGeometry
{
    public const float Acquisition = 16;
    public const float Release = 26;
    private const float AlignmentHysteresis = 6;

    #region Public API
    /// <summary>Prioritizes local grouping, retaining its target and side through alignment changes within a wider release band.</summary>
    public static IReadOnlyList<HudSnapCandidate> Candidates(RectangleF moving, PointF pointer,
        RectangleF viewport, IEnumerable<HudSnapTarget> targets, HudSnapCandidate? retained, bool bypass,
        IEnumerable<HudSnapBoundary>? boundaries = null)
    {
        if (bypass || moving.Width <= 0 || moving.Height <= 0) return Array.Empty<HudSnapCandidate>();
        var result = new List<HudSnapCandidate>();
        foreach (var target in targets.OrderBy(item => item.Container, StringComparer.Ordinal).ThenBy(item => item.Member, StringComparer.Ordinal))
        {
            var b = target.Bounds;
            string key = $"{target.Container.Length}:{target.Container}:{target.Member?.Length ?? -1}:{target.Member}";
            if (b.Width <= 0 || b.Height <= 0) continue;
            foreach (HudSnapSide side in Enum.GetValues<HudSnapSide>())
            {
                bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
                bool sameAttachment = retained?.Grouping == true && retained.Target!.Container == target.Container
                    && retained.Target.Member == target.Member && retained.Side == side;
                float fraction = horizontal ? (pointer.Y - b.Top) / b.Height : (pointer.X - b.Left) / b.Width;
                var alignment = SelectAlignment(fraction, horizontal ? b.Height : b.Width,
                    sameAttachment ? retained!.Alignment : null);
                float factor = alignment == HudOverlayCrossAlignment.Start ? 0 : alignment == HudOverlayCrossAlignment.End ? 1 : .5f;
                float x = horizontal ? side == HudSnapSide.Left ? b.Left - target.Gap - moving.Width : b.Right + target.Gap
                    : b.Left + factor * (b.Width - moving.Width);
                float y = horizontal ? b.Top + factor * (b.Height - moving.Height)
                    : side == HudSnapSide.Above ? b.Top - target.Gap - moving.Height : b.Bottom + target.Gap;
                // Grouping intent follows the pointer, independently of incoming size and grab offset.
                // Distance to the finite edge also prevents distant corner/span acquisition.
                float edge = side switch
                {
                    HudSnapSide.Left => b.Left,
                    HudSnapSide.Right => b.Right,
                    HudSnapSide.Above => b.Top,
                    _ => b.Bottom
                };
                float normal = (horizontal ? pointer.X : pointer.Y) - edge;
                float cross = horizontal ? pointer.Y - Math.Clamp(pointer.Y, b.Top, b.Bottom)
                    : pointer.X - Math.Clamp(pointer.X, b.Left, b.Right);
                float distance = MathF.Sqrt(normal * normal + cross * cross);
                string identity = $"group:{key}:{side}:{alignment}";
                Add(new HudSnapCandidate(identity, target, side, alignment, new PointF(x, y), distance, b));
            }
            // Matching edges and centers are alignment-only and require a nearby perpendicular span.
            if (moving.Bottom >= b.Top - Release && moving.Top <= b.Bottom + Release)
                AlignAxis(b, true, $"target:{key}");
            if (moving.Right >= b.Left - Release && moving.Left <= b.Right + Release)
                AlignAxis(b, false, $"target:{key}");
        }
        foreach (var boundary in (boundaries ?? Array.Empty<HudSnapBoundary>()).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var b = boundary.Bounds;
            if (!float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height)
                || b.Width <= 0 || b.Height <= 0) continue;
            string key = $"native:{boundary.Id.Length}:{boundary.Id}";
            // Native HUDs supply geometry only. Exterior edges let an overlay sit beside a meter instead of covering it.
            if (moving.Bottom >= b.Top - Release && moving.Top <= b.Bottom + Release)
            {
                AlignAxis(b, true, key, boundary);
                AlignExterior(boundary, true, key);
            }
            if (moving.Right >= b.Left - Release && moving.Left <= b.Right + Release)
            {
                AlignAxis(b, false, key, boundary);
                AlignExterior(boundary, false, key);
            }
        }
        AlignAxis(viewport, true, HudOverlayTargets.Screen);
        AlignAxis(viewport, false, HudOverlayTargets.Screen);
        // Grouping wins over alignment; native exterior docking wins over a single-axis guide that
        // could otherwise preserve an accidental gap. Retain destinations within each intent.
        return result.OrderByDescending(item => item.Grouping)
            .ThenByDescending(item => item.Exterior)
            .ThenByDescending(item => item.RetainsAttachment(retained) || item.Identity == retained?.Identity)
            .ThenBy(item => item.Distance).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();

        /// <summary>Applies acquisition or retained-release distance in physical pixels independently of GUI scale.</summary>
        void Add(HudSnapCandidate candidate)
        {
            bool keep = candidate.RetainsAttachment(retained) || candidate.Identity == retained?.Identity;
            if (candidate.Distance <= (keep ? Release : Acquisition)) result.Add(candidate);
        }
        /// <summary>Offers matching start, center, and end positions with guides on one physical axis.</summary>
        void AlignAxis(RectangleF reference, bool xAxis, string key, HudSnapBoundary? boundary = null)
        {
            foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
            {
                int index = (int)alignment;
                float factor = index * .5f;
                float coordinate = xAxis ? reference.Left + factor * (reference.Width - moving.Width)
                    : reference.Top + factor * (reference.Height - moving.Height);
                var origin = xAxis ? new PointF(coordinate, moving.Y) : new PointF(moving.X, coordinate);
                var guide = xAxis ? new RectangleF(coordinate + factor * moving.Width, Math.Min(moving.Top, reference.Top), 0, Math.Max(moving.Bottom, reference.Bottom) - Math.Min(moving.Top, reference.Top))
                    : new RectangleF(Math.Min(moving.Left, reference.Left), coordinate + factor * moving.Height, Math.Max(moving.Right, reference.Right) - Math.Min(moving.Left, reference.Left), 0);
                Add(new HudSnapCandidate($"align:{key}:{xAxis}:{index}", null, default, default, origin,
                    Math.Abs(xAxis ? moving.X - coordinate : moving.Y - coordinate), guide) { Axis = xAxis ? HudSnapAxis.X : HudSnapAxis.Y, GuideAlignment = alignment, Boundary = boundary });
            }
        }

        /// <summary>Offers outside-edge docking and a compatible cross-axis guide against a fixed HUD surface.</summary>
        void AlignExterior(HudSnapBoundary boundary, bool xAxis, string key)
        {
            var b = boundary.Bounds;
            foreach (bool before in new[] { true, false })
            {
                float edge = xAxis ? before ? b.Left : b.Right : before ? b.Top : b.Bottom;
                float coordinate = edge - (before ? xAxis ? moving.Width : moving.Height : 0);
                // Exterior docking establishes the normal axis first. Combine the nearest matching
                // cross-axis guide when it is within acquisition range, rather than allowing that
                // guide to win alone and preserve an accidental gap along the docking axis.
                float crossStart = xAxis ? moving.Y : moving.X;
                float crossCoordinate = crossStart;
                float crossDistance = float.PositiveInfinity;
                foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
                {
                    float factor = (int)alignment * .5f;
                    float aligned = xAxis ? b.Top + factor * (b.Height - moving.Height)
                        : b.Left + factor * (b.Width - moving.Width);
                    float distance = Math.Abs(crossStart - aligned);
                    if (distance <= Acquisition && distance < crossDistance)
                    {
                        crossDistance = distance;
                        crossCoordinate = aligned;
                    }
                }
                var origin = xAxis ? new PointF(coordinate, crossCoordinate) : new PointF(crossCoordinate, coordinate);
                var side = xAxis ? before ? HudSnapSide.Left : HudSnapSide.Right : before ? HudSnapSide.Above : HudSnapSide.Below;
                var guide = xAxis ? new RectangleF(edge, Math.Min(moving.Top, b.Top), 0, Math.Max(moving.Bottom, b.Bottom) - Math.Min(moving.Top, b.Top))
                    : new RectangleF(Math.Min(moving.Left, b.Left), edge, Math.Max(moving.Right, b.Right) - Math.Min(moving.Left, b.Left), 0);
                Add(new HudSnapCandidate($"align:{key}:outside:{side}", null, side, default, origin,
                    Math.Abs(xAxis ? moving.X - coordinate : moving.Y - coordinate), guide)
                    { Axis = xAxis ? HudSnapAxis.X : HudSnapAxis.Y, Boundary = boundary, Exterior = true });
            }
        }
    }
    #endregion

    #region Private
    /// <summary>Retains the acquired third across pointer jitter while preserving deliberate access to all alignments.</summary>
    private static HudOverlayCrossAlignment SelectAlignment(float fraction, float span, HudOverlayCrossAlignment? previous)
    {
        float margin = Math.Min(AlignmentHysteresis / span, 1f / 12);
        if (previous == HudOverlayCrossAlignment.Start && fraction <= 1f / 3 + margin) return previous.Value;
        if (previous == HudOverlayCrossAlignment.Center && fraction >= 1f / 3 - margin && fraction <= 2f / 3 + margin) return previous.Value;
        if (previous == HudOverlayCrossAlignment.End && fraction >= 2f / 3 - margin) return previous.Value;
        return fraction < 1f / 3 ? HudOverlayCrossAlignment.Start
            : fraction > 2f / 3 ? HudOverlayCrossAlignment.End : HudOverlayCrossAlignment.Center;
    }
    #endregion
}
