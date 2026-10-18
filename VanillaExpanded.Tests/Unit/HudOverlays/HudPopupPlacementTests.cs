using System.Drawing;
using VanillaExpanded.HudOverlays.Interaction;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks physical popup containment using caller-supplied synthetic geometry.</summary>
public sealed class HudPopupPlacementTests
{
    #region Public API
    /// <summary>Every edge and constrained viewport retains the complete visible rectangle inside its parent.</summary>
    [Theory]
    [InlineData(-10, -10, 30, 20, 0, 0, 100, 80)]
    [InlineData(100, -10, 30, 20, 0, 0, 100, 80)]
    [InlineData(-10, 80, 30, 20, 0, 0, 100, 80)]
    [InlineData(100, 80, 30, 20, 0, 0, 100, 80)]
    [InlineData(15, 20, 200, 100, 5, 9, 50, 30)]
    [InlineData(20, 25, 10, 15, 5, 9, 50, 60)]
    public void FitContainsAndPreservesAvailableExtent(float x, float y, float width, float height, float vx, float vy, float vw, float vh)
    {
        var viewport = new RectangleF(vx, vy, vw, vh);
        var fitted = HudPopupPlacement.Fit(new PointF(x, y), new SizeF(width, height), viewport);
        Assert.True(viewport.Contains(fitted));
        Assert.Equal(Math.Min(width, vw), fitted.Width); Assert.Equal(Math.Min(height, vh), fitted.Height);
        if (viewport.Contains(new RectangleF(x, y, width, height))) Assert.Equal(new RectangleF(x, y, width, height), fitted);
    }

    /// <summary>Invalid dimensions never produce a usable native surface.</summary>
    [Theory]
    [InlineData(0, 10, 100, 100)]
    [InlineData(10, -1, 100, 100)]
    [InlineData(10, 10, 0, 100)]
    [InlineData(float.NaN, 10, 100, 100)]
    public void InvalidGeometryIsEmpty(float width, float height, float vw, float vh)
    {
        Assert.Equal(RectangleF.Empty, HudPopupPlacement.Fit(PointF.Empty, new SizeF(width, height), new RectangleF(0, 0, vw, vh)));
    }
    #endregion
}
