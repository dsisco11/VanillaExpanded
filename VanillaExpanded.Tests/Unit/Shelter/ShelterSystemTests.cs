using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.Shelter;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.Shelter;

[Trait("Category", "Unit")]
[Collection("HudOverlayGeometry")]
public sealed class ShelterSystemTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationSupportsLiveGlobalAndFeatureToggles(bool global)
    {
        var config = VanillaExpandedModSystem.Config;
        bool previousGlobal = config.EnableHudOverlays, previousShelter = config.EnableShelterOverlay;
        var previousStyle = config.ShelterIndicatorStyle;
        var hud = new HudOverlaySystem();
        var system = new ShelterSystem();
        try
        {
            config.EnableHudOverlays = config.EnableShelterOverlay = true;
            var fixture = new BowAmmunitionSamplerTests.Fixture();
            fixture.Entity.Alive = true;
            var worldData = new Mock<IWorldPlayerData>();
            worldData.SetupProperty(value => value.CurrentGameMode, EnumGameMode.Survival);
            fixture.Player.SetupGet(value => value.WorldData).Returns(worldData.Object);
            var loader = new Mock<IModLoader>();
            loader.Setup(value => value.GetModSystem<HudOverlaySystem>(true)).Returns(hud);
            loader.Setup(value => value.GetModSystem<RoomRegistry>(true)).Returns(new RoomRegistry());
            fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
            hud.StartClientSide(fixture.Api.Object);
            system.StartClientSide(fixture.Api.Object);
            hud.Registry.BeginSession(fixture.Api.Object);
            ShelterOverlay? overlay = null;
            hud.Registry.RunPass(registration =>
            {
                Assert.Equal(ShelterSystem.OverlayId, registration.Id);
                Assert.Equal("player-status", registration.GroupId);
                Assert.Null(registration.ReservedSize);
                Assert.NotNull(registration.HoverLabelKey);
                overlay = Assert.IsType<ShelterOverlay>(registration.Overlay);
                Assert.True(registration.IsEnabled());
                Assert.True(registration.MenuSettings!.Enabled!.Value);
                Assert.Contains(HudIndicatorStyle.Text, registration.MenuSettings.Style!.Choices);
            });
            Assert.True(overlay!.IsApplicable());
            if (global) config.EnableHudOverlays = false;
            else config.EnableShelterOverlay = false;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.False(overlay.IsApplicable());
            config.EnableHudOverlays = config.EnableShelterOverlay = true;
            config.ShelterIndicatorStyle = HudIndicatorStyle.Text;
            system.OnConfigReloaded(fixture.Api.Object);
            Assert.True(overlay.IsApplicable());
            Assert.True(system.ShouldLoad(EnumAppSide.Client));
            Assert.False(system.ShouldLoad(EnumAppSide.Server));
            system.Dispose();
            hud.Registry.RunPass(_ => Assert.Fail("Disposed shelter registration remains active."));
        }
        finally
        {
            system.Dispose();
            hud.Dispose();
            config.EnableHudOverlays = previousGlobal;
            config.EnableShelterOverlay = previousShelter;
            config.ShelterIndicatorStyle = previousStyle;
        }
    }

    [Fact]
    public void ToggleDefaultsPersistsAndHasLocalizedConfigLibBinding()
    {
        Assert.True(JsonConvert.DeserializeObject<VanillaExpandedConfig>("{}")!.EnableShelterOverlay);
        var config = JsonConvert.DeserializeObject<VanillaExpandedConfig>("{\"EnableShelterOverlay\":false}")!;
        Assert.False(config.EnableShelterOverlay);
        Assert.False(JsonConvert.DeserializeObject<VanillaExpandedConfig>(JsonConvert.SerializeObject(config))!.EnableShelterOverlay);
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        string assets = Path.Combine(root!.FullName, "VanillaExpanded", "assets", "vanillaexpanded");
        var patch = JObject.Parse(File.ReadAllText(Path.Combine(assets, "config", "configlib-patches.json")));
        var language = JObject.Parse(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
        var setting = patch["settings"]!["boolean"]![nameof(VanillaExpandedConfig.EnableShelterOverlay)]!;
        Assert.True(setting["default"]!.Value<bool>());
        Assert.True(setting["clientSide"]!.Value<bool>());
        foreach (string field in new[] { "ingui", "comment" })
            Assert.False(string.IsNullOrWhiteSpace(language[setting[field]!.Value<string>()!.Split(':')[1]]!.Value<string>()));
        Assert.True(File.Exists(Path.Combine(assets, "textures", "hud", "shelter.svg")));
    }
}
