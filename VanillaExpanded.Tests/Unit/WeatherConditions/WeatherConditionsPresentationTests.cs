using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.WeatherConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.WeatherConditions;

/// <summary>Verifies composite-pack completeness and SVG fallback selection before native composition.</summary>
[Trait("Category", "Unit")]
public sealed class WeatherConditionsPresentationTests
{
    #region Public API
    /// <summary>Every required SVG must exist before composite layers are selected; an incomplete pack uses SVG fallbacks.</summary>
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
            : ["textures/hud/weather/rain.svg", "textures/hud/weather/fog.svg"];
        Assert.Equal(expectedLayers, presentation.Layers.Select(layer => layer.Path));

        // Act
        presentation.Prepare(context);

        // Assert
        Assert.Equal(expectedPack.Length, checkedPaths.Count);
    }

    /// <summary>An absent composite pack selects fallback SVGs without requesting any composite layer.</summary>
    [Fact]
    public void PrepareUsesFallbackSvgsWhenCompositePackIsAbsent()
    {
        // Arrange
        var api = new Mock<ICoreClientAPI>();
        var assets = new Mock<IAssetManager>();
        api.SetupGet(value => value.Assets).Returns(assets.Object);
        var checkedPaths = new List<string>();
        assets.Setup(manager => manager.Exists(It.IsAny<AssetLocation>()))
            .Callback<AssetLocation>(location => checkedPaths.Add(location.Path))
            .Returns(false);
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        using var presentation = new WeatherConditionsPresentation(loadBackdrop: _ => null);
        presentation.SetContent(new(EnumPrecipitationType.Rain, true) { ThunderIntensity = 1 });

        // Act
        presentation.Prepare(context);

        // Assert
        Assert.Equal(["textures/hud/weather/composite/rain-light.svg"], checkedPaths);
        Assert.Equal(
            ["textures/hud/weather/rain.svg", "textures/hud/weather/fog.svg"],
            presentation.Layers.Select(layer => layer.Path));
        Assert.DoesNotContain(presentation.Layers, layer => layer.Path.Contains("/composite/"));
    }

    /// <summary>Fallback SVG selection preserves every precipitation type, independent fog, full bounds and split bounds.</summary>
    [Theory]
    [InlineData(EnumPrecipitationType.Rain, false, "rain", null)]
    [InlineData(EnumPrecipitationType.Snow, false, "snow", null)]
    [InlineData(EnumPrecipitationType.Hail, false, "hail", null)]
    [InlineData(null, true, null, "fog")]
    [InlineData(EnumPrecipitationType.Rain, true, "rain", "fog")]
    [InlineData(EnumPrecipitationType.Snow, true, "snow", "fog")]
    [InlineData(EnumPrecipitationType.Hail, true, "hail", "fog")]
    public void PrepareSelectsSvgFallbackLayersAndLayout(
        EnumPrecipitationType? precipitation, bool fog, string? precipitationIcon, string? fogIcon)
    {
        // Arrange
        var api = new Mock<ICoreClientAPI>();
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        using var presentation = new WeatherConditionsPresentation(
            loadBackdrop: _ => null, hasCompletePack: _ => false);
        presentation.SetContent(new(precipitation, fog));

        // Act
        presentation.Prepare(context);

        // Assert
        string? precipitationPath = precipitationIcon is null ? null : $"textures/hud/weather/{precipitationIcon}.svg";
        string? fogPath = fogIcon is null ? null : $"textures/hud/weather/{fogIcon}.svg";
        string[] expectedPaths = new[] { precipitationPath, fogPath }.OfType<string>().ToArray();
        Assert.Equal(expectedPaths, presentation.Layers.Select(layer => layer.Path));
        Assert.False(presentation.Size.IsEmpty);

        var fullBounds = new RectangleF(0, 0, presentation.Size.Width, presentation.Size.Height);
        if (precipitationPath is not null && fogPath is not null)
        {
            float side = presentation.Size.Width / 2;
            float top = presentation.Size.Height / 4;
            Assert.Equal(new RectangleF(0, top, side, side), presentation.Layers[0].Bounds);
            Assert.Equal(new RectangleF(side, top, side, side), presentation.Layers[1].Bounds);
        }
        else
        {
            Assert.Equal(fullBounds, Assert.Single(presentation.Layers).Bounds);
        }
    }

    /// <summary>Clear conditions stay hidden and do not inspect or load an unavailable weather pack.</summary>
    [Fact]
    public void PrepareKeepsClearFallbackConditionsHidden()
    {
        // Arrange
        var api = new Mock<ICoreClientAPI>();
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        using var presentation = new WeatherConditionsPresentation(
            loadBackdrop: _ => null,
            hasCompletePack: _ => throw new InvalidOperationException("Clear weather must not inspect the pack."));
        presentation.SetContent(new(null, false));

        // Act
        presentation.Prepare(context);

        // Assert
        Assert.True(presentation.Size.IsEmpty);
        Assert.Empty(presentation.Layers);
    }
    #endregion
}
