using System;
using System.Collections.Generic;
using System.Drawing;

namespace VanillaExpanded.HudOverlays.Layout;

/// <summary>Resolves mixed pairs using current drawable measurements without modifying saved membership.</summary>
internal static class HudLayoutGeometry
{
    #region Public API
    /// <summary>Measures bottom-up then places top-down, collapsing absent leaves and their gaps.</summary>
    public static (SizeF Size, IReadOnlyDictionary<string, RectangleF> Rectangles) Resolve(
        HudLayoutNode node, IReadOnlyDictionary<string, SizeF> sizes, double padding = 0)
    {
        if (!double.IsFinite(padding) || padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
        var measured = new Dictionary<HudLayoutNode, SizeF>(ReferenceEqualityComparer.Instance);
        var rectangles = new Dictionary<string, RectangleF>(StringComparer.Ordinal);
        SizeF size = Measure(node, sizes, measured);
        if (measured[node].IsEmpty) return (SizeF.Empty, rectangles);
        Place(node, new PointF((float)padding, (float)padding), measured, rectangles);
        size = new SizeF(size.Width + (float)(2 * padding), size.Height + (float)(2 * padding));
        if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height)) throw new ArgumentOutOfRangeException(nameof(sizes));
        return (size, rectangles);
    }
    #endregion

    #region Private
    /// <summary>Omits a pair gap whenever either branch has no drawable footprint.</summary>
    private static SizeF Measure(HudLayoutNode node, IReadOnlyDictionary<string, SizeF> sizes, Dictionary<HudLayoutNode, SizeF> measured)
    {
        if (node == null || measured.Count >= 8192 || measured.ContainsKey(node) || !Enum.IsDefined(node.Direction)
            || !Enum.IsDefined(node.Alignment) || !double.IsFinite(node.Gap) || node.Gap < 0
            || node.Indicator == null && (node.First == null || node.Second == null))
            throw new InvalidOperationException("Invalid or cyclic HUD geometry.");
        measured.Add(node, SizeF.Empty);
        SizeF result;
        if (node.Indicator != null)
        {
            sizes.TryGetValue(node.Indicator, out result);
            if (!float.IsFinite(result.Width) || !float.IsFinite(result.Height) || result.Width < 0 || result.Height < 0)
                throw new ArgumentOutOfRangeException(nameof(sizes));
            if (result.Width == 0 || result.Height == 0) result = SizeF.Empty;
        }
        else
        {
            var a = Measure(node.First!, sizes, measured);
            var b = Measure(node.Second!, sizes, measured);
            result = a.IsEmpty ? b : b.IsEmpty ? a : node.Direction == HudOverlayDirection.Horizontal
                ? new SizeF(a.Width + b.Width + (float)node.Gap, Math.Max(a.Height, b.Height))
                : new SizeF(Math.Max(a.Width, b.Width), a.Height + b.Height + (float)node.Gap);
        }
        measured[node] = result;
        return result;
    }

    /// <summary>Applies cross-axis alignment in local unscaled coordinates, retaining nonnegative child offsets.</summary>
    private static void Place(HudLayoutNode node, PointF origin, Dictionary<HudLayoutNode, SizeF> sizes, Dictionary<string, RectangleF> rectangles)
    {
        SizeF size = sizes[node];
        if (size.IsEmpty) return;
        if (node.Indicator != null) { rectangles.Add(node.Indicator, new RectangleF(origin, size)); return; }
        var a = node.First!; var b = node.Second!;
        if (sizes[a].IsEmpty) { Place(b, origin, sizes, rectangles); return; }
        if (sizes[b].IsEmpty) { Place(a, origin, sizes, rectangles); return; }
        float factor = node.Alignment == HudOverlayCrossAlignment.Center ? .5f : node.Alignment == HudOverlayCrossAlignment.End ? 1 : 0;
        bool horizontal = node.Direction == HudOverlayDirection.Horizontal;
        float crossA = factor * (horizontal ? size.Height - sizes[a].Height : size.Width - sizes[a].Width);
        float crossB = factor * (horizontal ? size.Height - sizes[b].Height : size.Width - sizes[b].Width);
        float advance = (horizontal ? sizes[a].Width : sizes[a].Height) + (float)node.Gap;
        Place(a, new PointF(origin.X + (horizontal ? 0 : crossA), origin.Y + (horizontal ? crossA : 0)), sizes, rectangles);
        Place(b, new PointF(origin.X + (horizontal ? advance : crossB), origin.Y + (horizontal ? crossB : advance)), sizes, rectangles);
    }
    #endregion
}
