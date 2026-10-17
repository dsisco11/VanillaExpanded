using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Calculates physical-pixel candidates without changing membership or retaining pointer corrections.</summary>
internal static class HudSnapGeometry
{
    public const float Acquisition = 6;
    public const float Release = 10;

    #region Public API
    /// <summary>Ranks local adjacency and alignment candidates, retaining an acquired identity within its wider release band.</summary>
    public static IReadOnlyList<HudSnapCandidate> Candidates(RectangleF moving, PointF pointer,
        RectangleF viewport, IEnumerable<HudSnapTarget> targets, string? retained, bool bypass)
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
                // Adjacency requires overlapping perpendicular spans; distant corners are never docking requests.
                bool local = horizontal ? moving.Bottom >= b.Top && moving.Top <= b.Bottom : moving.Right >= b.Left && moving.Left <= b.Right;
                if (!local) continue;
                float fraction = horizontal ? (pointer.Y - b.Top) / b.Height : (pointer.X - b.Left) / b.Width;
                var alignment = fraction < 1f / 3 ? HudOverlayCrossAlignment.Start : fraction > 2f / 3 ? HudOverlayCrossAlignment.End : HudOverlayCrossAlignment.Center;
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
        return result.OrderByDescending(item => item.Identity == retained).ThenBy(item => item.Distance)
            .ThenByDescending(item => item.Grouping).ThenBy(item => item.Identity, StringComparer.Ordinal).ToArray();

        /// <summary>Applies acquisition or retained-release distance in physical pixels independently of GUI scale.</summary>
        void Add(HudSnapCandidate candidate)
        {
            if (candidate.Distance <= (candidate.Identity == retained ? Release : Acquisition)) result.Add(candidate);
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
}
