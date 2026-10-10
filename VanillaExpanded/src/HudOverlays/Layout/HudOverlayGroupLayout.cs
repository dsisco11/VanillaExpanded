using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Layout;

/// <summary>Packs an eligible ordered prefix into one native bounds hierarchy without owning presentation resources.</summary>
/// <remarks>The scheduler supplies enabled/applicable members; overflow suppression never changes their update eligibility.
/// Position-only changes modify native bounds and clips, without calling presentation preparation.</remarks>
internal sealed class HudOverlayGroupLayout : IDisposable
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, HudOverlayMemberLayout> children = new(StringComparer.Ordinal);
    private KeyValuePair<HudOverlayRegistration, SizeF>[] previous = Array.Empty<KeyValuePair<HudOverlayRegistration, SizeF>>();
    private HudOverlayGroup? definition;
    private RectangleF? target;
    private long contextRevision = -1;
    private readonly Dictionary<ElementBounds, (double X, double Y)> renderOffsets = new();
    private bool disposed;
    public ElementBounds Root { get; } = ElementBounds.Fixed(0, 0, 0, 0);
    public IReadOnlyList<HudOverlayMemberLayout> Members { get; private set; } = Array.Empty<HudOverlayMemberLayout>();
    public bool Available { get; private set; }
    public long Revision { get; private set; }

    #region Public API
    /// <summary>Consumes sampled measurements and updates native layout only when placement or participating content changes.</summary>
    /// <returns>Whether bounds, membership, clips, or target availability changed.</returns>
    public bool Apply(HudOverlayAnchorContext context, HudOverlayGroup group,
        IReadOnlyList<KeyValuePair<HudOverlayRegistration, SizeF>> eligibleMembers)
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("Native layout requires the creating client main thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(eligibleMembers);
        var ordered = eligibleMembers.OrderBy(member => member.Key.Order)
            .ThenBy(member => member.Key.Id, StringComparer.Ordinal).ToArray();
        foreach (var member in ordered)
        {
            if (member.Key.GroupId != group.Id) throw new ArgumentException("A member belongs to a different group.", nameof(eligibleMembers));
            ValidateSize(member.Value);
        }
        if (ordered.Select(member => member.Key.Id).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Members must have unique overlay IDs.", nameof(eligibleMembers));
        RectangleF? resolved = context.Resolve(group.Placement.TargetId);
        long revision = group.Placement.TargetId == HudOverlayAnchorContext.ScreenTargetId ? context.Revision : context.GeometryRevision;
        bool parentChanged = !ReferenceEquals(Root.ParentBounds, context.WindowBounds);
        if (!parentChanged && definition == group && contextRevision == revision && target == resolved && previous.SequenceEqual(ordered) && RenderOffsetsUnchanged())
            return false;

        // The native root is the only position authority; keep its parent stable across frames and detach on session replacement.
        if (parentChanged)
        {
            Root.ParentBounds?.ChildBounds.Remove(Root);
            Root.ParentBounds = null;
            context.WindowBounds.WithChild(Root);
        }
        definition = group;
        contextRevision = revision;
        target = resolved;
        previous = ordered;
        Revision++;
        Available = resolved.HasValue && ordered.Length > 0 && context.SafeRectangle.Width > 0 && context.SafeRectangle.Height > 0;
        if (!Available)
        {
            // Unavailable anchors suspend geometry, but removal must still release stale registration references.
            var eligible = ordered.ToDictionary(member => member.Key.Id, member => member.Key, StringComparer.Ordinal);
            foreach (string id in children.Keys.Where(id => !eligible.TryGetValue(id, out var registration)
                || !ReferenceEquals(children[id].Registration, registration)).ToArray())
            {
                Root.ChildBounds.Remove(children[id].Bounds);
                children.Remove(id);
            }
            Members = Array.Empty<HudOverlayMemberLayout>();
            CaptureRenderOffsets();
            return true;
        }

        double scale = context.GuiScale;
        HudOverlayPacking packing = group.Packing;
        int count = RetainedCount(ordered, packing, context.SafeRectangle.Size, scale);
        SizeF size = PackedSize(ordered, count, packing);
        SynchronizeChildren(ordered, count, packing, size);
        Root.fixedWidth = size.Width;
        Root.fixedHeight = size.Height;
        PositionRoot(context, group.Placement, resolved!.Value, size);
        Root.MarkDirtyRecursive();
        Root.CalcWorldBounds();

        // Shift the whole group, preserving every child offset. Oversized first members start at the safe edge and are clipped.
        RectangleF safe = context.SafeRectangle;
        double x = Math.Clamp(Root.renderX, safe.Left, Math.Max(safe.Left, safe.Right - Root.OuterWidth));
        double y = Math.Clamp(Root.renderY, safe.Top, Math.Max(safe.Top, safe.Bottom - Root.OuterHeight));
        Root.fixedOffsetX += (x - Root.renderX) / scale;
        Root.fixedOffsetY += (y - Root.renderY) / scale;
        Root.MarkDirtyRecursive();
        Root.CalcWorldBounds();
        foreach (HudOverlayMemberLayout member in Members)
            member.Clip = RectangleF.Intersect(safe, new RectangleF((float)member.Bounds.renderX, (float)member.Bounds.renderY,
                (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight));
        CaptureRenderOffsets();
        return true;
    }

    /// <summary>Detaches the native hierarchy without disposing borrowed parents or registered presentations.</summary>
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("Native layout cleanup requires the creating client main thread.");
        if (disposed) return;
        disposed = true;
        Root.ParentBounds?.ChildBounds.Remove(Root);
        Root.ParentBounds = null;
        Root.ChildBounds.Clear();
        children.Clear();
        renderOffsets.Clear();
        previous = Array.Empty<KeyValuePair<HudOverlayRegistration, SizeF>>();
        Members = Array.Empty<HudOverlayMemberLayout>();
        Available = false;
        definition = null;
        target = null;
    }
    #endregion

    #region Private
    #region Native invalidation
    /// <summary>Detects pixel render-offset changes on exposed native bounds before reusing cached clips.</summary>
    private bool RenderOffsetsUnchanged()
    {
        if (!renderOffsets.TryGetValue(Root, out var rootOffset) || rootOffset != (Root.renderOffsetX, Root.renderOffsetY)) return false;
        foreach (HudOverlayMemberLayout child in children.Values)
            if (!renderOffsets.TryGetValue(child.Bounds, out var offset) || offset != (child.Bounds.renderOffsetX, child.Bounds.renderOffsetY)) return false;
        return true;
    }

    /// <summary>Retains only native offset values needed for geometry invalidation, without presentation state.</summary>
    private void CaptureRenderOffsets()
    {
        renderOffsets.Clear();
        renderOffsets[Root] = (Root.renderOffsetX, Root.renderOffsetY);
        foreach (HudOverlayMemberLayout child in children.Values)
            renderOffsets[child.Bounds] = (child.Bounds.renderOffsetX, child.Bounds.renderOffsetY);
    }
    #endregion
    #region Packing
    /// <summary>Rejects invalid measurements before they reach native sizing arithmetic.</summary>
    private static void ValidateSize(SizeF size)
    {
        if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width < 0 || size.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Measurements must be finite and nonnegative GUI-unit dimensions.");
    }

    /// <summary>Finds the longest stable prefix fitting both axes; always retains an oversized first member for clipping.</summary>
    private static int RetainedCount(KeyValuePair<HudOverlayRegistration, SizeF>[] members, HudOverlayPacking packing, SizeF safe, double scale)
    {
        int count = 1;
        for (int candidate = 1; candidate <= members.Length; candidate++)
        {
            SizeF size = PackedSize(members, candidate, packing);
            if (size.Width * scale > safe.Width || size.Height * scale > safe.Height) break;
            count = candidate;
        }
        return count;
    }

    /// <summary>Includes content padding in fixed dimensions, leaving native root padding zero.</summary>
    private static SizeF PackedSize(KeyValuePair<HudOverlayRegistration, SizeF>[] members, int count, HudOverlayPacking packing)
    {
        double main = packing.Gap * Math.Max(0, count - 1), cross = 0;
        for (int i = 0; i < count; i++)
        {
            SizeF size = members[i].Value;
            main += packing.Direction == HudOverlayDirection.Horizontal ? size.Width : size.Height;
            cross = Math.Max(cross, packing.Direction == HudOverlayDirection.Horizontal ? size.Height : size.Width);
        }
        double padding = 2 * packing.Padding;
        double width = (packing.Direction == HudOverlayDirection.Horizontal ? main : cross) + padding;
        double height = (packing.Direction == HudOverlayDirection.Horizontal ? cross : main) + padding;
        if (width > float.MaxValue || height > float.MaxValue) throw new ArgumentOutOfRangeException(nameof(members), "Packed dimensions exceed supported geometry.");
        return new SizeF((float)width, (float)height);
    }

    /// <summary>Reuses retained native children and explicitly repacks after measured size or eligible membership changes.</summary>
    private void SynchronizeChildren(KeyValuePair<HudOverlayRegistration, SizeF>[] ordered, int count, HudOverlayPacking packing, SizeF size)
    {
        var kept = new HashSet<string>(ordered.Take(count).Select(member => member.Key.Id), StringComparer.Ordinal);
        foreach (string id in children.Keys.Where(id => !kept.Contains(id)).ToArray())
        {
            Root.ChildBounds.Remove(children[id].Bounds);
            children.Remove(id);
        }
        var layouts = new HudOverlayMemberLayout[count];
        double cursor = packing.Padding;
        for (int i = 0; i < count; i++)
        {
            var member = ordered[i];
            if (!children.TryGetValue(member.Key.Id, out HudOverlayMemberLayout? child) || !ReferenceEquals(child.Registration, member.Key))
            {
                if (child != null) Root.ChildBounds.Remove(child.Bounds);
                ElementBounds bounds = ElementBounds.Fixed(0, 0, member.Value.Width, member.Value.Height);
                Root.WithChild(bounds);
                children[member.Key.Id] = child = new HudOverlayMemberLayout(member.Key, bounds);
            }
            double crossSpace = packing.Direction == HudOverlayDirection.Horizontal
                ? size.Height - 2 * packing.Padding - member.Value.Height
                : size.Width - 2 * packing.Padding - member.Value.Width;
            double factor = packing.CrossAlignment == HudOverlayCrossAlignment.Center ? .5 : packing.CrossAlignment == HudOverlayCrossAlignment.End ? 1 : 0;
            double cross = packing.Padding + crossSpace * factor;
            child.Bounds.fixedX = packing.Direction == HudOverlayDirection.Horizontal ? cursor : cross;
            child.Bounds.fixedY = packing.Direction == HudOverlayDirection.Horizontal ? cross : cursor;
            child.Bounds.fixedWidth = member.Value.Width;
            child.Bounds.fixedHeight = member.Value.Height;
            cursor += (packing.Direction == HudOverlayDirection.Horizontal ? member.Value.Width : member.Value.Height) + packing.Gap;
            layouts[i] = child;
        }
        Members = Array.AsReadOnly(layouts);
    }
    #endregion
    #region Attachment
    /// <summary>Uses native screen alignment or converts named pixel attachment positions into fixed GUI units once.</summary>
    private void PositionRoot(HudOverlayAnchorContext context, HudOverlayPlacement placement, RectangleF rectangle, SizeF size)
    {
        PointF attachment = HudOverlayPoints.Normalized(placement.Attachment);
        PointF pivot = HudOverlayPoints.Normalized(placement.Pivot);
        Root.fixedX = Root.fixedY = Root.fixedOffsetX = Root.fixedOffsetY = 0;
        Root.absMarginX = Root.absMarginY = Root.absOffsetX = Root.absOffsetY = 0;
        Root.fixedPaddingX = Root.fixedPaddingY = 0;
        if (placement.TargetId == HudOverlayAnchorContext.ScreenTargetId)
        {
            Root.Alignment = HudOverlayPoints.Alignment(placement.Attachment);
            Root.fixedOffsetX = placement.OffsetX + (1 - 2 * attachment.X) * HudOverlayAnchorContext.SafeInset + (attachment.X - pivot.X) * size.Width;
            Root.fixedOffsetY = placement.OffsetY + (1 - 2 * attachment.Y) * HudOverlayAnchorContext.SafeInset + (attachment.Y - pivot.Y) * size.Height;
        }
        else
        {
            Root.Alignment = EnumDialogArea.None;
            // Native target render positions already contain ancestor render offsets; subtract the native window origin before parenting.
            Root.fixedX = (rectangle.X + attachment.X * rectangle.Width - context.WindowBounds.renderX) / context.GuiScale
                - pivot.X * size.Width + placement.OffsetX;
            Root.fixedY = (rectangle.Y + attachment.Y * rectangle.Height - context.WindowBounds.renderY) / context.GuiScale
                - pivot.Y * size.Height + placement.OffsetY;
        }
    }
    #endregion
    #endregion
}
