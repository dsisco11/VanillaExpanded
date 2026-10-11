using Moq;
using VanillaExpanded.BodyTemperature;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.BodyTemperature;

/// <summary>Checks the thin feature composition root against shared registration defaults.</summary>
[Collection("HudOverlayGeometry")]
public sealed class BodyTemperatureSystemTests
{
    #region Public API
    /// <summary>Registration follows client-only runtime startup and keeps the native placement and polling interval.</summary>
    [Fact]
    public void ClientRegistrationUsesSharedGroupAndBoundedInterval()
    {
        var api = new Mock<ICoreClientAPI>(); var events = new Mock<IClientEventAPI>(); var loader = new Mock<IModLoader>();
        api.SetupGet(value => value.Event).Returns(events.Object); api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var hud = new HudOverlaySystem(); var feature = new BodyTemperatureSystem();
        loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
        Assert.True(feature.ShouldLoad(EnumAppSide.Client)); Assert.False(feature.ShouldLoad(EnumAppSide.Server));
        Assert.True(feature.ExecuteOrder() > hud.ExecuteOrder());
        hud.StartClientSide(api.Object); feature.StartClientSide(api.Object);
        var group = Assert.Single(hud.Registry.GetGroups(), group => group.Id == "player-status");
        Assert.Equal("player-status", group.Id); Assert.Equal(HudOverlayDirection.Vertical, group.Packing.Direction);
        Assert.Equal(HudOverlayAnchorContext.HotbarTargetId, group.Placement.TargetId);
        Assert.Equal(HudOverlayPoint.LeftBottom, group.Placement.Attachment);
        Assert.Equal(HudOverlayPoint.RightBottom, group.Placement.Pivot);
        Assert.Equal(0, group.Placement.OffsetX); Assert.Equal(4, group.Placement.OffsetY);
        int visits = 0; hud.Registry.RunPass(registration =>
        {
            visits++; Assert.Equal(BodyTemperatureSystem.OverlayId, registration.Id);
            Assert.Equal(group.Id, registration.GroupId); Assert.Equal(250, registration.RefreshIntervalMs);
            Assert.IsType<BodyTemperatureOverlay>(registration.Overlay);
        });
        Assert.Equal(1, visits); feature.Dispose(); feature.Dispose();
        hud.Registry.RunPass(_ => Assert.Fail("Removed registration remained active."));
        hud.Dispose();
    }
    #endregion
}
