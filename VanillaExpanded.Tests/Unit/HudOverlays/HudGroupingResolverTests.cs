using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies immutable insertion and whole-tree merging without flattening either arrangement.</summary>
public sealed class HudGroupingResolverTests
{
    #region Public API
    /// <summary>Every side and alignment preserves both internal arrangements and creates nonoverlapping geometry.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WholeGroupMergePreservesBothTrees(int sideValue)
    {
        var side = (HudSnapSide)sideValue;
        var destination = Pair("target:a", "target:b", HudOverlayDirection.Vertical);
        var incoming = Pair("incoming:a", "incoming:b", HudOverlayDirection.Horizontal);
        var sizes = destination.Leaves().Concat(incoming.Leaves()).ToDictionary(value => value, _ => new SizeF(30, 20));
        foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
        {
            var merged = HudGroupingResolver.Insert(destination, incoming, null, side, alignment, 9);
            Assert.Contains(destination, new[] { merged.First, merged.Second }); Assert.Contains(incoming, new[] { merged.First, merged.Second });
            Assert.Equal(alignment, merged.Alignment); Assert.Equal(9, merged.Gap);
            Assert.Equal(4, merged.Leaves().Distinct().Count());
            var rectangles = HudLayoutGeometry.Resolve(merged, sizes).Rectangles.Values.ToArray();
            for (int i = 0; i < rectangles.Length; i++)
                for (int j = i + 1; j < rectangles.Length; j++) Assert.False(rectangles[i].IntersectsWith(rectangles[j]));
            var targetRect = HudLayoutGeometry.Resolve(merged, sizes).Rectangles["target:a"];
            var incomingRect = HudLayoutGeometry.Resolve(merged, sizes).Rectangles["incoming:a"];
            if (side == HudSnapSide.Left) Assert.True(incomingRect.Right <= targetRect.Left);
            if (side == HudSnapSide.Right) Assert.True(incomingRect.Left >= targetRect.Right);
            if (side == HudSnapSide.Above) Assert.True(incomingRect.Bottom <= targetRect.Top);
            if (side == HudSnapSide.Below) Assert.True(incomingRect.Top >= targetRect.Bottom);
        }
    }

    /// <summary>Individual insertion replaces only the selected leaf, preserving its surrounding relationship.</summary>
    [Fact]
    public void LeafInsertionRetainsUnaffectedBranchAndPairPolicy()
    {
        var destination = Pair("target:a", "target:b", HudOverlayDirection.Vertical);
        var incoming = new HudLayoutNode { Indicator = "incoming" };
        foreach (var side in Enum.GetValues<HudSnapSide>())
        foreach (var alignment in Enum.GetValues<HudOverlayCrossAlignment>())
        {
            var result = HudGroupingResolver.Insert(destination, incoming, "target:b", side, alignment, 7);
            Assert.Same(destination.First, result.First); Assert.Equal(destination.Direction, result.Direction); Assert.Equal(destination.Gap, result.Gap);
            bool before = side is HudSnapSide.Left or HudSnapSide.Above;
            Assert.Same(destination.Second, before ? result.Second!.Second : result.Second!.First); Assert.Same(incoming, before ? result.Second.First : result.Second.Second);
            Assert.Equal(alignment, result.Second.Alignment); Assert.Equal(side is HudSnapSide.Left or HudSnapSide.Right ? HudOverlayDirection.Horizontal : HudOverlayDirection.Vertical, result.Second.Direction);
            Assert.Equal(new[] { "target:a", "target:b" }, destination.Leaves());
            Assert.Equal(new[] { "incoming", "target:a", "target:b" }, result.Leaves().Order());
        }
    }
    #endregion

    #region Private
    /// <summary>Builds a mixed-arrangement source with an authored gap retained by merging.</summary>
    private static HudLayoutNode Pair(string a, string b, HudOverlayDirection direction) => new()
    {
        First = new HudLayoutNode { Indicator = a }, Second = new HudLayoutNode { Indicator = b }, Direction = direction,
        Alignment = HudOverlayCrossAlignment.Center, Gap = 4
    };
    #endregion
}
