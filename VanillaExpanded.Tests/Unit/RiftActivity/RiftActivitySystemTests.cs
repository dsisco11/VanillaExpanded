using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.RiftActivity;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.RiftActivity;

/// <summary>Checks actual registration and live enable composition without loading graphics services.</summary>
[Collection("HudOverlayGeometry")]
public sealed class RiftActivitySystemTests
{
    #region Public API
    /// <summary>Feature and global toggles immediately disable the registered overlay, retaining its toolbar anchor.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveToggleAndNativeAnchor(bool global)
    {
        var config = VanillaExpandedModSystem.Config;
        bool previousGlobal = config.EnableHudOverlays, previousRift = config.EnableRiftActivityOverlay;
        var hud = new HudOverlaySystem();
        var system = new RiftActivitySystem();
        try
        {
            config.EnableHudOverlays = config.EnableRiftActivityOverlay = true;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            fixture.Entity.Alive = true;
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            loader.Setup(value => value.GetModSystem<Vintagestory.GameContent.ModSystemRiftWeather>(true)).Returns(new Vintagestory.GameContent.ModSystemRiftWeather());
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object);
            system.StartClientSide(fixture.Api.Object);
            var group = hud.Registry.GetGroups().Single(value => value.Id == "player-status");
            Assert.Equal(HudOverlayAnchorContext.HotbarTargetId, group.Placement.TargetId);
            Assert.Equal(HudOverlayPoint.LeftBottom, group.Placement.Attachment);
            Assert.Equal(HudOverlayPoint.RightBottom, group.Placement.Pivot);
            Assert.Equal(VanillaExpanded.HudOverlays.Layout.HudOverlayDirection.Horizontal, group.Packing.Direction);
            hud.Registry.BeginSession(fixture.Api.Object);
            RiftActivityOverlay? overlay = null;
            hud.Registry.RunPass(registration =>
            {
                Assert.Equal(RiftActivitySystem.OverlayId, registration.Id);
                Assert.Equal(1000, registration.RefreshIntervalMs);
                Assert.Equal(90, registration.Order);
                Assert.Equal(new System.Drawing.SizeF(24, 24), registration.ReservedSize);
                Assert.Equal("player-status", registration.GroupId);
                overlay = Assert.IsType<RiftActivityOverlay>(registration.Overlay);
            });
            Assert.True(overlay!.IsApplicable());
            if (global) config.EnableHudOverlays = false; else config.EnableRiftActivityOverlay = false;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.False(overlay.IsApplicable());
            Assert.Equal(System.Drawing.SizeF.Empty, overlay.Measure());
            config.EnableHudOverlays = config.EnableRiftActivityOverlay = true;
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
            config.EnableRiftActivityOverlay = previousRift;
        }
    }
    #endregion
}


