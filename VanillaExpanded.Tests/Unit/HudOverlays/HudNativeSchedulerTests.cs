using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Updating;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks publication of prepared native snapshots independently of sampling and view ownership.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudNativeSchedulerTests
{
    #region Public API
    /// <summary>Content revisions advance only for changed preparation; style revisions track successful style changes.</summary>
    [Fact]
    public void PreparedRevisionsSeparateContentMeasurementAndStructure()
    {
        using var native = new HudNativeComponentFixture();
        var api = native.Api.Object; using var registry = new HudOverlayRegistry();
        registry.RegisterGroup(new("group", new("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
        var overlay = new Mock<IHudOverlay>(); overlay.As<INativePresentation>();
        var style = new Mock<IHudOverlayStyleSetting>();
        Enum value = HudIndicatorStyle.Icon; style.SetupGet(setting => setting.Value).Returns(() => value);
        var registration = new HudOverlayRegistration("example:native", overlay.Object, () => true, "group",
            new HudOverlayMenuSettings(Style: style.Object), refreshIntervalMs: 100);
        registry.Register(registration); registry.BeginSession(api);
        var window = ElementBounds.Fixed(0, 0, 500, 400).WithEmptyParent(); window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => 1, () => null); anchors.BeginFrame();
        var failures = new List<Exception>();
        long now = 0; var scheduler = new HudOverlayScheduler(registry, () => now, (_, error) => failures.Add(error));
        var context = new HudOverlayPreparationContext(api, 1, "en");
        overlay.Setup(item => item.IsApplicable()).Returns(true);
        overlay.Setup(item => item.Measure()).Returns(new SizeF(57, 63));
        HudOverlayChange change = HudOverlayChange.None;
        overlay.Setup(item => item.Refresh()).Returns(() => change);
        scheduler.Update(anchors, context, true);
        Assert.Empty(failures); Assert.True(scheduler.IsDrawable(registration));
        long initial = scheduler.PresentationRevision(registration), structure = scheduler.StructureRevision(registration);
        now += 100; scheduler.Update(anchors, context, true);
        Assert.Equal(initial, scheduler.PresentationRevision(registration));
        change = HudOverlayChange.Measurement;
        now += 100; scheduler.Update(anchors, context, true);
        Assert.True(scheduler.PresentationRevision(registration) > initial);
        Assert.Equal(structure, scheduler.StructureRevision(registration));
        value = HudIndicatorStyle.Text; change = HudOverlayChange.Presentation | HudOverlayChange.Measurement;
        now += 100; scheduler.Update(anchors, context, true);
        Assert.True(scheduler.StructureRevision(registration) > structure);
        overlay.Invocations.Clear();
        var nextContext = new HudOverlayPreparationContext(api, 2, "fr", 19);
        scheduler.PrepareContext(nextContext); scheduler.PrepareContext(nextContext);
        overlay.Verify(item => item.Prepare(nextContext), Times.Once);
        overlay.Verify(item => item.Measure(), Times.Once);
        overlay.Verify(item => item.Refresh(), Times.Never);
        overlay.Verify(item => item.IsApplicable(), Times.Never);
    }
    #endregion
}
