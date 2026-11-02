using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WeatherConditions;

/// <summary>Verifies composite-pack completeness and PNG-only fallback selection before native composition.</summary>
[Trait("Category", "Unit")]
public sealed class WeatherConditionsPresentationTests
{
    #region Public API
    /// <summary>Every required SVG must exist before composite layers are selected; an incomplete pack uses PNG fallbacks.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrepareChecksTheCompleteSvgPackAndSelectsCompatibleArtwork(bool complete)
    {
        // Arrange
        var api = new Mock<ICoreClientAPI>();
        var assets = new Mock<IAssetManager>();
        api.SetupGet(value => value.Assets).Returns(assets.Object);
        string[] names =
        [
            "rain-light", "rain-moderate", "rain-heavy", "snow-light", "snow-moderate", "snow-heavy",
            "hail-light", "hail-moderate", "hail-heavy",
            "fog-light", "fog-moderate", "fog-dense", "thunder-light", "thunder-moderate", "thunder-heavy"
        ];
        string[] expectedPack = names
            .Select(name => "textures/hud/weather/composite/" + name + ".svg").ToArray();
        var checkedPaths = new List<string>();
        assets.Setup(manager => manager.Exists(It.IsAny<AssetLocation>()))
            .Callback<AssetLocation>(location => checkedPaths.Add(location.Path))
            .Returns((AssetLocation location) => complete || location.Path != expectedPack[^1]);
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        using var presentation = new WeatherConditionsPresentation(loadBackdrop: _ => null);
        presentation.SetContent(new(EnumPrecipitationType.Rain, true) { ThunderIntensity = 1 });

        // Act
        presentation.Prepare(context);

        // Assert
        Assert.Equal(expectedPack, checkedPaths);
        string[] expectedLayers = complete
            ? [
                "textures/hud/weather/composite/rain-light.svg",
                "textures/hud/weather/composite/fog-light.svg",
                "textures/hud/weather/composite/thunder-light.svg"
            ]
            : ["textures/hud/weather/rain.png", "textures/hud/weather/fog.png"];
        Assert.Equal(expectedLayers, presentation.Layers.Select(layer => layer.Path));

        // Act
        presentation.Prepare(context);

        // Assert
        Assert.Equal(expectedPack.Length, checkedPaths.Count);
    }
    #endregion
}
