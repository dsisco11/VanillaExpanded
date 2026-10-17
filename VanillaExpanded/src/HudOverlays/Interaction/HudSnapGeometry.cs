using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        RectangleF viewport, IEnumerable<HudSnapTarget> targets, HudSnapCandidate? retained, bool bypass)
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
                float reach = sameAttachment ? Release : Acquisition;
                // Nearby corners acquire naturally; the wider retained band also prevents a one-pixel span exit
                // from abruptly losing a destination while its normal edge remains within magnetic reach.
                bool local = horizontal ? moving.Bottom >= b.Top - reach && moving.Top <= b.Bottom + reach
                    : moving.Right >= b.Left - reach && moving.Left <= b.Right + reach;
                if (!local) continue;
                float fraction = horizontal ? (pointer.Y - b.Top) / b.Height : (pointer.X - b.Left) / b.Width;
                var alignment = SelectAlignment(fraction, horizontal ? b.Height : b.Width,
                    sameAttachment ? retained!.Alignment : null);
                float factor = alignment == HudOverlayCrossAlignment.Start ? 0 : alignment == HudOverlayCrossAlignment.End ? 1 : .5f;
                float x = horizontal ? side == HudSnapSide.Left ? b.Left - target.Gap - moving.Width : b.Right + target.Gap
                    : b.Left + factor * (b.Width - moving.Width);
                float y = horizontal ? b.Top + factor * (b.Height - moving.Height)
                    : side == HudSnapSide.Above ? b.Top - target.Gap - moving.Height : b.Bottom + target.Gap;
                float distance = Math.Abs(horizontal ? moving.X - x : moving.Y - y);
                string identity = $"group:{key}:{side}:{alignment}";
                Add(new HudSnapCandidate(identity, target, side, alignment, new PointF(x, y), distance, b));
            }
            // Matching edges and centers are alignment-only and require a nearby perpendicular span.
            if (moving.Bottom >= b.Top - Release && moving.Top <= b.Bottom + Release)
                AlignAxis(b, true, "target:" + key);
            if (moving.Right >= b.Left - Release && moving.Left <= b.Right + Release)
                AlignAxis(b, false, "target:" + key);
        }
        AlignAxis(viewport, true, "screen");
        AlignAxis(viewport, false, "screen");
        // A screen guide cannot hold an indicator away from a nearby valid grouping target. Within each
        // intent, retain the acquired destination/side before considering a closer competing destination.
        return result.OrderByDescending(item => item.Grouping)
            .ThenByDescending(item => item.RetainsAttachment(retained) || item.Identity == retained?.Identity)
            .ThenBy(item => item.Distance).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();

        /// <summary>Applies acquisition or retained-release distance in physical pixels independently of GUI scale.</summary>
        void Add(HudSnapCandidate candidate)
        {
            bool keep = candidate.RetainsAttachment(retained) || candidate.Identity == retained?.Identity;
            if (candidate.Distance <= (keep ? Release : Acquisition)) result.Add(candidate);
        }
        /// <summary>Offers matching start, center, and end positions with guides on one physical axis.</summary>
        void AlignAxis(RectangleF reference, bool xAxis, string key)
        {
            for (int index = 0; index < 3; index++)
            {
                float factor = index * .5f;
                float coordinate = xAxis ? reference.Left + factor * (reference.Width - moving.Width)
                    : reference.Top + factor * (reference.Height - moving.Height);
                var origin = xAxis ? new PointF(coordinate, moving.Y) : new PointF(moving.X, coordinate);
                var guide = xAxis ? new RectangleF(coordinate + factor * moving.Width, Math.Min(moving.Top, reference.Top), 0, Math.Max(moving.Bottom, reference.Bottom) - Math.Min(moving.Top, reference.Top))
                    : new RectangleF(Math.Min(moving.Left, reference.Left), coordinate + factor * moving.Height, Math.Max(moving.Right, reference.Right) - Math.Min(moving.Left, reference.Left), 0);
                Add(new HudSnapCandidate($"align:{key}:{xAxis}:{index}", null, default, default, origin,
                    Math.Abs(xAxis ? moving.X - coordinate : moving.Y - coordinate), guide) { AlignsX = xAxis });
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
