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
    private string? retained;
    private HudOverlayGroupMovement? capture;
    private long captureRevision;
    public HudSnapCandidate? Selected { get; private set; }
    public RectangleF Destination { get; private set; }
    public RectangleF Insertion { get; private set; }

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
        foreach (var candidate in HudSnapGeometry.Candidates(raw, pointer, safe, targets, retained, bypass))
        {
            if (!candidate.Grouping)
            {
                var origin = RoundedClamped(candidate.Origin, raw.Size, viewport, safe, scale);
                var final = new RectangleF(viewport.X + origin.X * (float)scale, viewport.Y + origin.Y * (float)scale, raw.Width, raw.Height);
                // Integer native checkpoints may lose an apparent alignment; never show a guide for that result.
                bool xAxis = candidate.AlignsX;
                if (Math.Abs(xAxis ? final.X - candidate.Origin.X : final.Y - candidate.Origin.Y) > .01f) continue;
                containers.Move(origin);
                Selected = candidate;
                Insertion = final;
                break;
            }
            var target = containers.Resolved.First(item => item.Id == candidate.Target!.Container);
            var targetLayout = layouts[target.Id];
            var tree = HudGroupingResolver.Insert(target.Tree, source.Tree, candidate.Target!.Member,
                candidate.Side, candidate.Alignment, containers.Definition(target).Packing.Gap);
            var geometry = HudLayoutGeometry.Resolve(tree, sizes, target.Padding);
            var originPixels = Bounds(targetLayout).Location;
            var originGui = RoundedClamped(originPixels, new SizeF(geometry.Size.Width * (float)scale, geometry.Size.Height * (float)scale), viewport, safe, scale);
            var rectangles = geometry.Rectangles.ToDictionary(item => item.Key, item => new RectangleF(
                viewport.X + (originGui.X + item.Value.X) * (float)scale, viewport.Y + (originGui.Y + item.Value.Y) * (float)scale,
                item.Value.Width * (float)scale, item.Value.Height * (float)scale));
            if (!Valid(rectangles, layouts, source.Id, target.Id, safe)) continue;
            if (!containers.PreviewJoin(candidate, originGui)) continue;
            Selected = candidate;
            Destination = Union(rectangles.Where(item => !excluded.Contains(item.Key)).Select(item => item.Value));
            Insertion = Union(rectangles.Where(item => excluded.Contains(item.Key)).Select(item => item.Value));
            break;
        }
        retained = Selected?.Identity;
    }
    #endregion

    #region Private
    /// <summary>Uses the native root's physical extents, excluding transient controls and shadows.</summary>
    private static RectangleF Bounds(HudOverlayGroupLayout layout) => new((float)layout.Root.renderX, (float)layout.Root.renderY,
        (float)layout.Root.OuterWidth, (float)layout.Root.OuterHeight);

    /// <summary>Reconciles integer native origins with the same reachability clamp as the native layout.</summary>
    private static PointF RoundedClamped(PointF pixels, SizeF size, RectangleF viewport, RectangleF safe, double scale)
    {
        double x = (pixels.X - viewport.X) / scale, y = (pixels.Y - viewport.Y) / scale;
        double left = (safe.Left - viewport.Left) / scale, top = (safe.Top - viewport.Top) / scale;
        return new PointF((int)Math.Clamp(x, left, Math.Min(int.MaxValue, Math.Max(left, (safe.Right - viewport.Left - size.Width) / scale))),
            (int)Math.Clamp(y, top, Math.Min(int.MaxValue, Math.Max(top, (safe.Bottom - viewport.Top - size.Height) / scale))));
    }

    /// <summary>Rejects clipping and overlaps introduced by insertion, including collisions with the source remainder.</summary>
    private static bool Valid(IReadOnlyDictionary<string, RectangleF> rectangles,
        IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts, string source, string target, RectangleF viewport)
    {
        var values = rectangles.Values.ToArray();
        for (int i = 0; i < values.Length; i++)
        {
            if (!viewport.Contains(values[i])) return false;
            for (int j = i + 1; j < values.Length; j++) if (Overlaps(values[i], values[j])) return false;
            foreach (var other in layouts.Where(item => item.Key != source && item.Key != target && item.Value.Available))
                foreach (var member in other.Value.Members)
                    if (member.Clip.Width > 0 && member.Clip.Height > 0 && Overlaps(values[i], member.Clip)) return false;
        }
        return true;
    }

    /// <summary>Allows touching edges while rejecting positive-area intersections.</summary>
    private static bool Overlaps(RectangleF a, RectangleF b) => Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > .01f
        && Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > .01f;

    /// <summary>Builds feedback extents from the final visible member rectangles.</summary>
    private static RectangleF Union(IEnumerable<RectangleF> rectangles)
    {
        RectangleF result = RectangleF.Empty;
        foreach (var rectangle in rectangles) result = result.IsEmpty ? rectangle : RectangleF.Union(result, rectangle);
        return result;
    }
    #endregion
}
