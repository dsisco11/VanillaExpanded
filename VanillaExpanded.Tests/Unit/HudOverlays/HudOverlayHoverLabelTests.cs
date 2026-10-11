using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks hover eligibility without composing text or issuing graphics calls.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayHoverLabelTests
{
    #region Public API
    /// <summary>Only a labeled visible member under a free pointer exposes a hover target.</summary>
    [Theory]
    [InlineData(false, true, true, true, 25, 25, 20, true)]
    [InlineData(true, true, true, true, 25, 25, 20, false)]
    [InlineData(false, false, true, true, 25, 25, 20, false)]
    [InlineData(false, true, false, true, 25, 25, 20, false)]
    [InlineData(false, true, true, false, 25, 25, 20, false)]
    [InlineData(false, true, true, true, 50, 20, 20, false)]
    [InlineData(false, true, true, true, 25, 25, 0, false)]
    public void HitTestingRequiresVisibleLabeledMember(bool grabbed, bool available, bool drawable,
        bool labeled, float x, float y, float width, bool expected)
    {
        float oldScale = RuntimeEnv.GUIScale;
        int oldLeft = GuiStyle.LeftDialogMargin;
        try
        {
            RuntimeEnv.GUIScale = 1;
            GuiStyle.LeftDialogMargin = 0;
            var registration = new HudOverlayRegistration("test:indicator", Mock.Of<IHudOverlay>(), () => true, "group",
                hoverLabelKey: labeled ? "test:label" : null);
            using var group = new HudOverlayGroupLayout();
            var window = ElementBounds.Fixed(0, 0, 200, 100).WithEmptyParent();
            window.CalcWorldBounds();
            var anchors = new HudOverlayAnchorContext(window, () => 1, () => null);
            anchors.BeginFrame();
            group.Apply(anchors, new HudOverlayGroup("group", new HudOverlayPlacement(available ? "screen" : "hotbar",
                HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, 5, 5)),
                new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(width, 20)) });
            var result = HudOverlayHoverLabels.FindTarget(new[] { group }, _ => drawable, grabbed, new PointF(x, y));
            Assert.Equal(expected, result == registration);
        }
        finally
        {
            RuntimeEnv.GUIScale = oldScale;
            GuiStyle.LeftDialogMargin = oldLeft;
        }
    }
    #endregion
}
