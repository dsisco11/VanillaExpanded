using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.ApproximateTime;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks actual registration and live enable composition without loading graphics services.</summary>
[Collection("HudOverlayGeometry")]
public sealed class ApproximateTimeSystemTests
{
    #region Public API
    /// <summary>Feature and global toggles immediately disable the registered overlay, retaining its toolbar anchor.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveToggleAndNativeAnchor(bool global)
    {
        var config = VanillaExpandedModSystem.Config;
        bool previousGlobal = config.EnableHudOverlays, previousTime = config.EnableApproximateTimeOverlay;
        var hud = new HudOverlaySystem();
        var system = new ApproximateTimeSystem();
        try
        {
            config.EnableHudOverlays = config.EnableApproximateTimeOverlay = true;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            fixture.Entity.Alive = true;
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object);
            system.StartClientSide(fixture.Api.Object);
            var group = hud.Registry.GetGroups().Single(value => value.Id == "player-status");
            Assert.Equal(HudOverlayAnchorContext.HotbarTargetId, group.Placement.TargetId);
            Assert.Equal(HudOverlayPoint.LeftBottom, group.Placement.Attachment);
            Assert.Equal(HudOverlayPoint.RightBottom, group.Placement.Pivot);
            Assert.Equal(VanillaExpanded.HudOverlays.Layout.HudOverlayDirection.Horizontal, group.Packing.Direction);
            hud.Registry.BeginSession(fixture.Api.Object);
            ApproximateTimeOverlay? overlay = null;
            hud.Registry.RunPass(registration =>
            {
                Assert.Equal(ApproximateTimeSystem.OverlayId, registration.Id);
                Assert.Equal(1000, registration.RefreshIntervalMs);
                Assert.Equal(100, registration.Order);
                Assert.Null(registration.ReservedSize);
                Assert.Equal("player-status", registration.GroupId);
                overlay = Assert.IsType<ApproximateTimeOverlay>(registration.Overlay);
            });
            Assert.True(overlay!.IsApplicable());
            if (global) config.EnableHudOverlays = false; else config.EnableApproximateTimeOverlay = false;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.False(overlay.IsApplicable());
            Assert.Equal(System.Drawing.SizeF.Empty, overlay.Measure());
            config.EnableHudOverlays = config.EnableApproximateTimeOverlay = true;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.True(overlay.IsApplicable());
            Assert.True(system.ShouldLoad(EnumAppSide.Client));
            Assert.False(system.ShouldLoad(EnumAppSide.Server));
        }
        finally
        {
            system.Dispose();
            hud.Dispose();
            config.EnableHudOverlays = previousGlobal;
            config.EnableApproximateTimeOverlay = previousTime;
        }
    }
    #endregion
}

