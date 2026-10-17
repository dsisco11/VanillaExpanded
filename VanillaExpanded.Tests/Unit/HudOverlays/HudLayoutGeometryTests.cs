using System.Drawing;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks mixed arrangements independently of shipped artwork dimensions.</summary>
public sealed class HudLayoutGeometryTests
{
    #region Public API
    /// <summary>Every directional ordering and alignment preserves the supplied gap and cross alignment.</summary>
    [Theory]
    [InlineData(false, false, HudOverlayCrossAlignment.Start)] [InlineData(false, false, HudOverlayCrossAlignment.Center)] [InlineData(false, false, HudOverlayCrossAlignment.End)]
    [InlineData(false, true, HudOverlayCrossAlignment.Start)] [InlineData(false, true, HudOverlayCrossAlignment.Center)] [InlineData(false, true, HudOverlayCrossAlignment.End)]
    [InlineData(true, false, HudOverlayCrossAlignment.Start)] [InlineData(true, false, HudOverlayCrossAlignment.Center)] [InlineData(true, false, HudOverlayCrossAlignment.End)]
    [InlineData(true, true, HudOverlayCrossAlignment.Start)] [InlineData(true, true, HudOverlayCrossAlignment.Center)] [InlineData(true, true, HudOverlayCrossAlignment.End)]
    public void PairResolvesAllSidesAndAlignments(bool vertical, bool reverse, HudOverlayCrossAlignment alignment)
    {
        var a = new HudLayoutNode { Indicator = "test:a" }; var b = new HudLayoutNode { Indicator = "test:b" };
        const float gap = 7, padding = 3;
        var node = new HudLayoutNode { First = reverse ? b : a, Second = reverse ? a : b, Direction = vertical ? HudOverlayDirection.Vertical : HudOverlayDirection.Horizontal, Alignment = alignment, Gap = gap };
        var sizes = new Dictionary<string, SizeF> { ["test:a"] = new(19, 43), ["test:b"] = new(31, 17) };
        var result = HudLayoutGeometry.Resolve(node, sizes, padding);
        var first = result.Rectangles[node.First.Indicator!]; var second = result.Rectangles[node.Second.Indicator!];
        Assert.Equal(gap, vertical ? second.Top - first.Bottom : second.Left - first.Right);
        float factor = alignment == HudOverlayCrossAlignment.Start ? 0 : alignment == HudOverlayCrossAlignment.Center ? .5f : 1;
        Assert.Equal(vertical ? first.Left + factor * first.Width : first.Top + factor * first.Height, vertical ? second.Left + factor * second.Width : second.Top + factor * second.Height);
        Assert.All(result.Rectangles.Values, bounds => Assert.True(new RectangleF(PointF.Empty, result.Size).Contains(bounds)));
        Assert.False(first.IntersectsWith(second));
    }
    /// <summary>Hidden branches collapse their gaps without changing saved trees, and reappear at the original relationship.</summary>
    [Fact]
    public void HiddenIntermediatesCollapseAndReappearWithoutPruning()
    {
        var tree = HudLayoutNode.FromMembers(new[] { "test:a", "test:b", "test:c" }, new HudOverlayPacking(HudOverlayDirection.Horizontal, gap: 9))!;
        var sizes = new Dictionary<string, SizeF> { ["test:a"] = new(10, 20), ["test:b"] = new(30, 20), ["test:c"] = new(40, 20) };
        var before = HudLayoutGeometry.Resolve(tree, sizes);
        sizes.Remove("test:b"); var hidden = HudLayoutGeometry.Resolve(tree, sizes);
        Assert.True(hidden.Size.Width < before.Size.Width); Assert.Equal(before.Rectangles["test:a"], hidden.Rectangles["test:a"]);
        Assert.Equal(new[] { "test:a", "test:b", "test:c" }, tree.Leaves());
        sizes["test:b"] = new(30, 20); Assert.Equal(before.Size, HudLayoutGeometry.Resolve(tree, sizes).Size);
        sizes.Clear(); Assert.Empty(HudLayoutGeometry.Resolve(tree, sizes).Rectangles);
    }
    /// <summary>Resizing moves connected neighbors while preserving local origin and native gap relationships.</summary>
    [Fact]
    public void ResizePropagatesAndTrueRemovalPrunesOnlyItsBranch()
    {
        var tree = HudLayoutNode.FromMembers(new[] { "test:a", "test:b", "test:c" }, new HudOverlayPacking(HudOverlayDirection.Horizontal, gap: 5))!;
        var sizes = new Dictionary<string, SizeF> { ["test:a"] = new(10, 20), ["test:b"] = new(30, 20), ["test:c"] = new(40, 20) };
        var before = HudLayoutGeometry.Resolve(tree, sizes); sizes["test:a"] = new(25, 20);
        var after = HudLayoutGeometry.Resolve(tree, sizes);
        Assert.Equal(before.Rectangles["test:a"].Location, after.Rectangles["test:a"].Location);
        Assert.Equal(15, after.Rectangles["test:c"].Left - before.Rectangles["test:c"].Left);
        Assert.Equal(new[] { "test:a", "test:c" }, tree.Remove("test:b")!.Leaves());
    }
    #endregion
}
