using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks native exterior docking precedence and compatible two-axis placement.</summary>
public sealed class HudNativeDockingGeometryTests
{
    #region Public API
    /// <summary>Every exterior side corrects its normal gap and any acquired perpendicular alignment.</summary>
    [Theory]
    [InlineData(0, 0, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(0, .5f, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(0, 1, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)]
    [InlineData(1, 0, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(1, .5f, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(1, 1, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)]
    [InlineData(2, 0, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(2, .5f, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(2, 1, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)]
    [InlineData(3, 0, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(3, .5f, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(3, 1, 2, 6, 300, 300, 240, 180, 40, 30, 1200, 900)]
    [InlineData(0, .5f, 20, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(1, .5f, 20, 6, 300, 300, 240, 180, 40, 30, 1200, 900)]
    [InlineData(2, .5f, 20, 6, 300, 300, 240, 180, 40, 30, 1200, 900)] [InlineData(3, .5f, 20, 6, 300, 300, 240, 180, 40, 30, 1200, 900)]
    public void ExteriorDockingCombinesOnlyAcquiredCrossAxisGuides(int sideValue, float factor, float crossOffset, float normalGap,
        float x, float y, float width, float height, float movingWidth, float movingHeight, float viewportWidth, float viewportHeight)
    {
        var side = (HudSnapSide)sideValue;
        var boundary = new HudSnapBoundary("saturation", new RectangleF(x, y, width, height));
        var size = new SizeF(movingWidth, movingHeight);
        bool horizontal = side is HudSnapSide.Left or HudSnapSide.Right;
        float cross = horizontal ? boundary.Bounds.Top + factor * (boundary.Bounds.Height - size.Height)
            : boundary.Bounds.Left + factor * (boundary.Bounds.Width - size.Width);
        float normal = side switch
        {
            HudSnapSide.Left => boundary.Bounds.Left - size.Width,
            HudSnapSide.Right => boundary.Bounds.Right,
            HudSnapSide.Above => boundary.Bounds.Top - size.Height,
            _ => boundary.Bounds.Bottom
        };
        float rawNormal = normal + (side is HudSnapSide.Left or HudSnapSide.Above ? -normalGap : normalGap);
        var moving = new RectangleF(horizontal ? rawNormal : cross + crossOffset,
            horizontal ? cross + crossOffset : rawNormal, size.Width, size.Height);
        var viewport = new RectangleF(0, 0, viewportWidth, viewportHeight);
        var candidate = HudSnapGeometry.Candidates(moving, moving.Location, viewport,
            Array.Empty<HudSnapTarget>(), null, false, new[] { boundary }).First();
        Assert.True(candidate.Exterior);
        Assert.Equal(side, candidate.Side);
        Assert.Equal(normal, horizontal ? candidate.Origin.X : candidate.Origin.Y);
        Assert.Equal(cross + (crossOffset <= HudSnapGeometry.Acquisition ? 0 : crossOffset),
            horizontal ? candidate.Origin.Y : candidate.Origin.X);
        var attachment = Assert.IsType<VanillaExpanded.HudOverlays.Anchoring.HudOverlayPlacement>(
            HudSnapAttachment.FromSnap(candidate, new RectangleF(candidate.Origin, size), viewport, 1));
        Assert.Equal(0, horizontal ? attachment.OffsetX : attachment.OffsetY);
    }

    /// <summary>A perfectly matched or retained axis guide cannot preserve an unintended normal gap.</summary>
    [Theory]
    [InlineData(false, 6, 300, 500, 240, 20, 40, 40, 1200, 900)]
    [InlineData(true, 6, 300, 500, 240, 20, 40, 40, 1200, 900)]
    public void ExteriorDockingOutranksZeroDistanceAxisGuide(bool retainAxisGuide, float gap,
        float x, float y, float width, float height, float movingWidth, float movingHeight, float viewportWidth, float viewportHeight)
    {
        var boundary = new HudSnapBoundary("saturation", new RectangleF(x, y, width, height));
        var moving = new RectangleF(boundary.Bounds.Left, boundary.Bounds.Top - movingHeight - gap, movingWidth, movingHeight);
        var viewport = new RectangleF(0, 0, viewportWidth, viewportHeight);
        var candidates = HudSnapGeometry.Candidates(moving, moving.Location, viewport,
            Array.Empty<HudSnapTarget>(), null, false, new[] { boundary });
        var axisGuide = Assert.Single(candidates, value => !value.Exterior && value.Boundary == boundary
            && value.Axis == HudSnapAxis.X && value.Distance == 0);
        var chosen = HudSnapGeometry.Candidates(moving, moving.Location, viewport,
            Array.Empty<HudSnapTarget>(), retainAxisGuide ? axisGuide : null, false, new[] { boundary }).First();
        Assert.True(chosen.Exterior);
        Assert.Equal(HudSnapSide.Above, chosen.Side);
        Assert.Equal(boundary.Bounds.Top, chosen.Origin.Y + moving.Height);
        Assert.Equal(boundary.Bounds.Left, chosen.Origin.X);
    }
    #endregion
}
