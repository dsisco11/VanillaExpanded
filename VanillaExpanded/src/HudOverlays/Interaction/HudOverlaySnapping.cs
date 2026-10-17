using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Reconciles measured geometry with immutable grouping previews, without sampling or preparing feature artwork.</summary>
internal sealed class HudOverlaySnapping
{
    private readonly HudOverlayContainers containers;
    private HudSnapCandidate? retained;
    private HudOverlayGroupMovement? capture;
    private long captureRevision;
    public HudSnapCandidate? Selected { get; private set; }
    public RectangleF Destination { get; private set; }
    public RectangleF Insertion { get; private set; }
    public bool DropAllowed { get; private set; } = true;

    #region Public API
    /// <summary>Borrows the session's authoritative transaction owner.</summary>
    public HudOverlaySnapping(HudOverlayContainers containers) { this.containers = containers; }

    /// <summary>Clears transient feedback without retaining a candidate across unrelated drags.</summary>
    public void Clear()
    {
        retained = null;
        capture = null;
        Selected = null;
        Destination = Insertion = RectangleF.Empty;
        DropAllowed = true;
    }

    /// <summary>Applies the first valid ranked proposal using the same resolved geometry used by native layout.</summary>
    public void Update(HudOverlayGroupMovement movement, PointF pointer, bool bypass, double scale, RectangleF viewport,
        IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts, Func<HudOverlayRegistration, bool> drawable)
    {
        if (!ReferenceEquals(capture, movement) || captureRevision != movement.DragRevision)
        {
            retained = null;
            capture = movement;
            captureRevision = movement.DragRevision;
        }
        Selected = null;
        Destination = Insertion = RectangleF.Empty;
        if (!movement.Dragging || !layouts.TryGetValue(movement.Id, out var moving) || !moving.Available)
        { Clear(); return; }
        var source = containers.Resolved.First(item => item.Id == movement.Id);
        var safe = HudOverlayAnchorContext.Inset(viewport, HudOverlayAnchorContext.SafeInset * scale);
        var excluded = source.Tree.Leaves().ToHashSet(StringComparer.Ordinal);
        var targets = new List<HudSnapTarget>();
        var sizes = new Dictionary<string, SizeF>(StringComparer.Ordinal);
        foreach (var pair in layouts)
        {
            if (!pair.Value.Available) continue;
            var record = containers.Resolved.FirstOrDefault(item => item.Id == pair.Key);
            if (record == null) continue;
            foreach (var member in pair.Value.Members.Where(item => drawable(item.Registration)))
                sizes[member.Registration.Id] = new SizeF((float)member.Bounds.fixedWidth, (float)member.Bounds.fixedHeight);
            if (pair.Key == source.Id || record.Tree.Leaves().Any(excluded.Contains)) continue;
            float gap = (float)(containers.Definition(record).Packing.Gap * scale);
            if (movement.IndicatorId == null)
            {
                if (pair.Value.Members.Any(item => sizes.ContainsKey(item.Registration.Id) && item.Clip.Width > 0 && item.Clip.Height > 0))
                    targets.Add(new HudSnapTarget(pair.Key, null, Bounds(pair.Value), gap));
            }
            else foreach (var member in pair.Value.Members.Where(item => sizes.ContainsKey(item.Registration.Id) && item.Clip.Width > 0 && item.Clip.Height > 0))
                targets.Add(new HudSnapTarget(pair.Key, member.Registration.Id, new RectangleF((float)member.Bounds.renderX,
                    (float)member.Bounds.renderY, (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight), gap));
        }
        // Use the raw total displacement, not the previous snap or the clamped native root, for acquisition.
        var raw = new RectangleF(viewport.X + movement.UnsnappedOrigin.X * (float)scale,
            viewport.Y + movement.UnsnappedOrigin.Y * (float)scale, (float)moving.Root.OuterWidth, (float)moving.Root.OuterHeight);
        // Unsnapped drops must also be collision-free. Shift is the explicit override for intentional overlap.
        DropAllowed = bypass || !Collides(moving.Members.Where(member => drawable(member.Registration)).Select(member =>
            new RectangleF((float)member.Bounds.renderX, (float)member.Bounds.renderY,
                (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight)), layouts, source.Id, string.Empty);
        DropAllowed &= bypass || !RemainderCollides(movement, layouts, source.Id, string.Empty, drawable);
        foreach (var candidate in HudSnapGeometry.Candidates(raw, pointer, safe, targets, retained, bypass))
        {
            if (!candidate.Grouping)
            {
                var origin = RoundedClamped(candidate.Origin, raw.Size, viewport, safe, scale);
                var final = new RectangleF(viewport.X + origin.X * (float)scale, viewport.Y + origin.Y * (float)scale, raw.Width, raw.Height);
                // Native integer checkpoints need not land exactly on a fractional guide. Keep the magnet within
                // one GUI unit of the ideal and draw the guide at the actual placement, including boundary rounding.
                bool xAxis = candidate.AlignsX;
                float correction = xAxis ? final.X - candidate.Origin.X : final.Y - candidate.Origin.Y;
                if (Math.Abs(correction) > scale + .01f) continue;
                // Alignment changes placement without membership, but must obey the same collision rule as grouping.
                // Translate individual footprints rather than treating empty spaces in a mixed group as solid content.
                var aligned = moving.Members.Where(member => drawable(member.Registration)).ToDictionary(
                    member => member.Registration.Id, member => new RectangleF(
                        final.X + (float)(member.Bounds.renderX - moving.Root.renderX),
                        final.Y + (float)(member.Bounds.renderY - moving.Root.renderY),
                        (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight));
                if (!Valid(aligned, layouts, source.Id, string.Empty, safe) || RemainderCollides(movement, layouts, source.Id, string.Empty, drawable)) continue;
                containers.Move(origin);
                DropAllowed = true;
                var guide = candidate.Guide;
                if (xAxis) guide.X += correction; else guide.Y += correction;
                Selected = candidate with { Origin = final.Location, Guide = guide };
                Insertion = final;
                break;
            }
            var target = containers.Resolved.First(item => item.Id == candidate.Target!.Container);
            var targetLayout = layouts[target.Id];
            var tree = HudGroupingResolver.Insert(target.Tree, source.Tree, candidate.Target!.Member,
                candidate.Side, candidate.Alignment, containers.Definition(target).Packing.Gap);
            var geometry = HudLayoutGeometry.Resolve(tree, sizes, target.Padding);
            var expandedSize = new SizeF(geometry.Size.Width * (float)scale, geometry.Size.Height * (float)scale);
            var originPixels = ExpandedOrigin(Bounds(targetLayout), expandedSize, candidate.Side, candidate.Alignment);
            var originGui = RoundedClamped(originPixels, expandedSize, viewport, safe, scale);
            var rectangles = geometry.Rectangles.ToDictionary(item => item.Key, item => new RectangleF(
                viewport.X + (originGui.X + item.Value.X) * (float)scale, viewport.Y + (originGui.Y + item.Value.Y) * (float)scale,
                item.Value.Width * (float)scale, item.Value.Height * (float)scale));
            if (!Valid(rectangles, layouts, source.Id, target.Id, safe) || RemainderCollides(movement, layouts, source.Id, target.Id, drawable)) continue;
            if (!containers.PreviewJoin(candidate, originGui)) continue;
            DropAllowed = true;
            Selected = candidate;
            Destination = Union(rectangles.Where(item => !excluded.Contains(item.Key)).Select(item => item.Value));
            Insertion = Union(rectangles.Where(item => excluded.Contains(item.Key)).Select(item => item.Value));
            break;
        }
        retained = Selected;
    }
    #endregion

    #region Private
    #region Placement geometry
    /// <summary>Uses the native root's physical extents, excluding transient controls and shadows.</summary>
    private static RectangleF Bounds(HudOverlayGroupLayout layout) => new((float)layout.Root.renderX, (float)layout.Root.renderY,
        (float)layout.Root.OuterWidth, (float)layout.Root.OuterHeight);

    /// <summary>Grows toward the attachment while retaining the opposite edge and selected cross-axis anchor.</summary>
    private static PointF ExpandedOrigin(RectangleF existing, SizeF expanded, HudSnapSide side, HudOverlayCrossAlignment alignment)
    {
        // Keeping the former top-left for a left/top insertion pushes the destination away from the incoming item.
        // Rebase only by the added extent; cross-axis alignment retains its start, center, or end anchor.
        float factor = alignment == HudOverlayCrossAlignment.Start ? 0 : alignment == HudOverlayCrossAlignment.End ? 1 : .5f;
        bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
        return new PointF(existing.Left - (horizontal ? side == HudSnapSide.Left ? expanded.Width - existing.Width : 0
                : factor * (expanded.Width - existing.Width)),
            existing.Top - (horizontal ? factor * (expanded.Height - existing.Height)
                : side == HudSnapSide.Above ? expanded.Height - existing.Height : 0));
    }

    /// <summary>Reconciles integer native origins with the same reachability clamp as the native layout.</summary>
    private static PointF RoundedClamped(PointF pixels, SizeF size, RectangleF viewport, RectangleF safe, double scale)
    {
        double x = (pixels.X - viewport.X) / scale, y = (pixels.Y - viewport.Y) / scale;
        double left = (safe.Left - viewport.Left) / scale, top = (safe.Top - viewport.Top) / scale;
        return new PointF((int)Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), left, Math.Min(int.MaxValue, Math.Max(left, (safe.Right - viewport.Left - size.Width) / scale))),
            (int)Math.Clamp(Math.Round(y, MidpointRounding.AwayFromZero), top, Math.Min(int.MaxValue, Math.Max(top, (safe.Bottom - viewport.Top - size.Height) / scale))));
    }

    #endregion
    #region Collision validation
    /// <summary>Rejects clipping and overlaps introduced by insertion, including collisions with the source remainder.</summary>
    private static bool Valid(IReadOnlyDictionary<string, RectangleF> rectangles,
        IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts, string source, string target, RectangleF viewport)
    {
        var values = rectangles.Values.ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            if (!viewport.Contains(values[i])) return false;
            for (int j = i + 1; j < values.Length; j++) if (Overlaps(values[i], values[j])) return false;
        }
        return !Collides(values, layouts, source, target);
    }

    /// <summary>Checks visible peer content without treating empty group space or hover chrome as an obstacle.</summary>
    private static bool Collides(IEnumerable<RectangleF> rectangles,
        IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts, string source, string target, string? additionalExcluded = null)
    {
        var peers = layouts.Where(item => item.Key != source && item.Key != target && item.Key != additionalExcluded && item.Value.Available)
            .SelectMany(item => item.Value.Members).Where(member => member.Clip.Width > 0 && member.Clip.Height > 0)
            .Select(member => member.Clip).ToArray();
        return rectangles.Any(rectangle => peers.Any(peer => Overlaps(rectangle, peer)));
    }

    /// <summary>Checks reflowed source members against peers; a grouping target is tested through its final candidate geometry instead.</summary>
    private static bool RemainderCollides(HudOverlayGroupMovement movement,
        IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts, string source, string target, Func<HudOverlayRegistration, bool> drawable)
    {
        // Stable members identify the remainder even when singleton normalization changes its container identity.
        return layouts.Where(pair => pair.Key != source
            && pair.Value.Members.Any(member => movement.StartingMembers.Contains(member.Registration.Id)))
            .Any(pair => Collides(pair.Value.Members.Where(member => drawable(member.Registration)).Select(member => new RectangleF(
                (float)member.Bounds.renderX, (float)member.Bounds.renderY,
                (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight)), layouts, pair.Key, source, target));
    }
    /// <summary>Allows touching edges while rejecting positive-area intersections.</summary>
    private static bool Overlaps(RectangleF a, RectangleF b) => Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > .01f
        && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > .01f;

    #endregion
    #region Feedback geometry
    /// <summary>Builds feedback extents from the final visible member rectangles.</summary>
    private static RectangleF Union(IEnumerable<RectangleF> rectangles)
    {
        RectangleF result = RectangleF.Empty;
        foreach (var rectangle in rectangles) result = result.IsEmpty ? rectangle : RectangleF.Union(result, rectangle);
        return result;
    }
    #endregion
    #endregion
}
