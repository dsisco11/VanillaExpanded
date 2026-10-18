using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks fixed native HUD magnets without granting them grouping authority.</summary>
public sealed class HudNativeBoundaryGeometryTests
{
    #region Public API
    /// <summary>Each exterior native edge provides an alignment-only position at the supplied boundary.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)]
    [InlineData(2)] [InlineData(3)]
    public void ExteriorEdgesNeverBecomeGroupingTargets(int sideValue)
    {
        var side = (HudSnapSide)sideValue;
        var boundary = new HudSnapBoundary("native:meter", new RectangleF(240, 230, 110, 35));
        var size = new SizeF(44, 28);
        var ideal = side switch
        {
            HudSnapSide.Left => new PointF(boundary.Bounds.Left - size.Width, boundary.Bounds.Top),
            HudSnapSide.Right => new PointF(boundary.Bounds.Right, boundary.Bounds.Top),
            HudSnapSide.Above => new PointF(boundary.Bounds.Left, boundary.Bounds.Top - size.Height),
            _ => new PointF(boundary.Bounds.Left, boundary.Bounds.Bottom)
        };
        var moving = new RectangleF(ideal.X + 2, ideal.Y + 2, size.Width, size.Height);
        var candidates = HudSnapGeometry.Candidates(moving, ideal, new RectangleF(0, 0, 900, 700), Array.Empty<HudSnapTarget>(), null, false, new[] { boundary });
        var candidate = Assert.Single(candidates, item => item.Boundary == boundary && item.Exterior && item.Side == side);
        Assert.False(candidate.Grouping); Assert.Null(candidate.Target);
        if (candidate.Axis == HudSnapAxis.X) Assert.Equal(ideal.X, candidate.Origin.X); else Assert.Equal(ideal.Y, candidate.Origin.Y);
        Assert.Empty(HudSnapGeometry.Candidates(moving, ideal, new RectangleF(0, 0, 900, 700), Array.Empty<HudSnapTarget>(), candidate, true, new[] { boundary }));
    }

    /// <summary>Native start, center and end lines use caller geometry on both axes without changing membership.</summary>
    [Theory]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    public void NativeMatchingEdgesAndCentersUseMeasuredBounds(bool xAxis, int alignment)
    {
        var boundary = new HudSnapBoundary("meter", new RectangleF(240, 230, 110, 70));
        var moving = new RectangleF(250, 240, 44, 28);
        float factor = alignment * .5f;
        float expected = xAxis ? boundary.Bounds.Left + factor * (boundary.Bounds.Width - moving.Width)
            : boundary.Bounds.Top + factor * (boundary.Bounds.Height - moving.Height);
        if (xAxis) moving.X = expected + 2; else moving.Y = expected + 2;
        var candidates = HudSnapGeometry.Candidates(moving, moving.Location, new RectangleF(0, 0, 900, 700), Array.Empty<HudSnapTarget>(), null, false, new[] { boundary });
        var candidate = Assert.Single(candidates, item => item.Boundary == boundary && !item.Exterior && item.Axis == (xAxis ? HudSnapAxis.X : HudSnapAxis.Y)
            && Math.Abs((xAxis ? item.Origin.X : item.Origin.Y) - expected) < .001);
        Assert.False(candidate.Grouping); Assert.Null(candidate.Target);
    }
    /// <summary>Native edge retention uses the magnetic release band and disappears when that native surface is no longer present.</summary>
    [Fact]
    public void NativeMagnetsHaveHysteresisAndNoStaleGeometry()
    {
        var boundary = new HudSnapBoundary("health", new RectangleF(240, 230, 110, 35));
        var moving = new RectangleF(boundary.Bounds.Right, boundary.Bounds.Top, 44, 28);
        var viewport = new RectangleF(0, 0, 900, 700);
        var candidate = HudSnapGeometry.Candidates(moving, moving.Location, viewport, Array.Empty<HudSnapTarget>(), null, false, new[] { boundary }).First(item => item.Exterior && item.Side == HudSnapSide.Right);
        moving.X += (HudSnapGeometry.Acquisition + HudSnapGeometry.Release) / 2;
        Assert.DoesNotContain(HudSnapGeometry.Candidates(moving, moving.Location, viewport, Array.Empty<HudSnapTarget>(), null, false, new[] { boundary }), item => item.Identity == candidate.Identity);
        Assert.Contains(HudSnapGeometry.Candidates(moving, moving.Location, viewport, Array.Empty<HudSnapTarget>(), candidate, false, new[] { boundary }), item => item.Identity == candidate.Identity);
        Assert.DoesNotContain(HudSnapGeometry.Candidates(moving, moving.Location, viewport, Array.Empty<HudSnapTarget>(), candidate, false), item => item.Boundary != null);
    }
    #endregion
}
