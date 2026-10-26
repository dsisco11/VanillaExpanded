using System.Drawing;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks native stack parentage and invalidation using caller-supplied dimensions.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudIndicatorStackLayoutTests
{
    #region Public API
    /// <summary>Rows retain native parentage and follow mutations of the same host at several GUI scales.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void NativeRowsFollowMutableHostAndMeasuredContent(double scale)
    {
        var stack = new HudIndicatorStackLayout();
        stack.Configure(new SizeF(41, 13), new SizeF(27, 29), 7);
        Assert.Same(stack.Reading.ParentBounds, stack.Icon.ParentBounds);
        var host = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
        host.absFixedX = 19; host.absFixedY = 31;
        host.absInnerWidth = 240; host.absInnerHeight = 210; host.Initialized = true;
        stack.Resolve(host, scale);
        double originalX = stack.Reading.renderX, originalY = stack.Reading.renderY;
        Assert.Equal(41 * scale, stack.Reading.OuterWidth, 5);
        Assert.Equal(13 * scale, stack.Reading.OuterHeight, 5);
        Assert.Equal(host.renderX + host.OuterWidth / 2, stack.Reading.renderX + stack.Reading.OuterWidth / 2, 5);
        Assert.Equal(stack.Reading.renderY + stack.Reading.OuterHeight + 7 * scale, stack.Icon.renderY, 5);
        stack.Resolve(host, scale);
        Assert.Equal(originalX, stack.Reading.renderX);
        host.absFixedX += 17; host.absFixedY += 23;
        stack.Resolve(host, scale);
        Assert.Equal(originalX + 17, stack.Reading.renderX, 5);
        Assert.Equal(originalY + 23, stack.Reading.renderY, 5);
        stack.Configure(SizeF.Empty, new SizeF(27, 29), 7);
        stack.Resolve(host, scale);
        Assert.Equal(new SizeF(27, 29), stack.Size);
        Assert.Equal(host.renderY + (host.OuterHeight - 29 * scale) / 2, stack.Icon.renderY, 5);
    }
    #endregion
}
