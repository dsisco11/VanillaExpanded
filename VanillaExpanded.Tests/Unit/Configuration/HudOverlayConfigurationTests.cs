using System.Text.Json;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Checks persistent HUD settings, ConfigLib mappings, and native placement conversion.</summary>
public sealed class HudOverlayConfigurationTests
{
    #region Public API
    /// <summary>Absent settings preserve documented defaults through the existing loading path.</summary>
    [Fact]
    public void MissingConfigurationPersistsHudDefaults()
    {
        var api = new Mock<ICoreAPI>(); api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        var config = VanillaExpandedModSystem.LoadConfig(api.Object);
        Assert.True(config.EnableHudOverlays); Assert.True(config.EnableBowAmmunitionOverlay);
        Assert.Equal("saturation", config.HeldItemStatusAnchor);
        Assert.Equal(-4, config.HeldItemStatusOffsetX); Assert.Equal(0, config.HeldItemStatusOffsetY);
        api.Verify(value => value.StoreModConfig(config, Constants.ConfigFileName), Times.Once);
    }

    /// <summary>Stable keys and ConfigLib numeric selections resolve to exactly the same placement.</summary>
    [Theory]
    [InlineData("saturation", 0, "saturation", (int)HudOverlayPoint.LeftTop, (int)HudOverlayPoint.LeftBottom)]
    [InlineData("hotbar", 1, "hotbar", (int)HudOverlayPoint.RightMiddle, (int)HudOverlayPoint.LeftMiddle)]
    [InlineData("screen-left-top", 2, "screen", (int)HudOverlayPoint.LeftTop, (int)HudOverlayPoint.LeftTop)]
    [InlineData("screen-center-top", 3, "screen", (int)HudOverlayPoint.CenterTop, (int)HudOverlayPoint.CenterTop)]
    [InlineData("screen-right-top", 4, "screen", (int)HudOverlayPoint.RightTop, (int)HudOverlayPoint.RightTop)]
    [InlineData("screen-left-middle", 5, "screen", (int)HudOverlayPoint.LeftMiddle, (int)HudOverlayPoint.LeftMiddle)]
    [InlineData("screen-center-middle", 6, "screen", (int)HudOverlayPoint.CenterMiddle, (int)HudOverlayPoint.CenterMiddle)]
    [InlineData("screen-right-middle", 7, "screen", (int)HudOverlayPoint.RightMiddle, (int)HudOverlayPoint.RightMiddle)]
    [InlineData("screen-left-bottom", 8, "screen", (int)HudOverlayPoint.LeftBottom, (int)HudOverlayPoint.LeftBottom)]
    [InlineData("screen-center-bottom", 9, "screen", (int)HudOverlayPoint.CenterBottom, (int)HudOverlayPoint.CenterBottom)]
    [InlineData("screen-right-bottom", 10, "screen", (int)HudOverlayPoint.RightBottom, (int)HudOverlayPoint.RightBottom)]
    public void AnchorKeysAgreeWithNativePlacement(string key, int numeric, string target, int attachment, int pivot)
    {
        var config = new VanillaExpandedConfig { HeldItemStatusAnchor = key, HeldItemStatusOffsetX = 17.5f, HeldItemStatusOffsetY = -9.25f };
        var placement = HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config);
        Assert.Equal(target, placement.TargetId); Assert.Equal((HudOverlayPoint)attachment, placement.Attachment); Assert.Equal((HudOverlayPoint)pivot, placement.Pivot);
        Assert.Equal(17.5, placement.OffsetX); Assert.Equal(-9.25, placement.OffsetY);
        ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.HeldItemStatusAnchor)),
            ["Value"] = new IntAttribute(numeric)
        });
        Assert.Equal(key, config.HeldItemStatusAnchor);
        using var persisted = JsonDocument.Parse(JsonSerializer.Serialize(config));
        Assert.Equal(key, persisted.RootElement.GetProperty(nameof(VanillaExpandedConfig.HeldItemStatusAnchor)).GetString());
        Assert.Equal(placement, HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config));
        using var asset = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "VanillaExpanded/assets/vanillaexpanded/config/configlib-patches.json")));
        var setting = asset.RootElement.GetProperty("settings").EnumerateObject().SelectMany(category => category.Value.EnumerateObject()).Single(value => value.Name == nameof(VanillaExpandedConfig.HeldItemStatusAnchor)).Value;
        Assert.Equal(numeric, setting.GetProperty("mapping").GetProperty(key).GetInt32());
        Assert.Equal("saturation", setting.GetProperty("default").GetString());
    }

    /// <summary>Unknown keys and nonfinite coordinates fall back independently, with canonical persisted anchor keys.</summary>
    [Fact]
    public void InvalidValuesFallBackAndAnchorPersistenceIsCanonical()
    {
        var config = new VanillaExpandedConfig { HeldItemStatusAnchor = "unknown", HeldItemStatusOffsetX = float.NaN, HeldItemStatusOffsetY = 23 };
        var placement = HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config);
        Assert.Equal("saturation", placement.TargetId); Assert.Equal(-4, placement.OffsetX); Assert.Equal(23, placement.OffsetY);
        Assert.Equal("saturation", config.HeldItemStatusAnchor); Assert.True(float.IsNaN(config.HeldItemStatusOffsetX));
        config.HeldItemStatusOffsetX = 19; config.HeldItemStatusOffsetY = float.PositiveInfinity;
        placement = HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config);
        Assert.Equal(19, placement.OffsetX); Assert.Equal(0, placement.OffsetY);
        config.HeldItemStatusOffsetX = float.NegativeInfinity;
        Assert.Equal(-4, HudOverlayConfiguration.ResolveHeldItemStatusPlacement(config).OffsetX);
    }
    #endregion

    #region Private
    /// <summary>Locates checked-in configuration assets from an isolated or ordinary test output directory.</summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VanillaExpanded.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
    #endregion
}
