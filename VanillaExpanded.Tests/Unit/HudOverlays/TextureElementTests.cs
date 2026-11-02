using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies generated SVG and engine-cached PNG texture ownership at the native texture boundary.</summary>
[Trait("Category", "Unit")]
[Collection("HudOverlayGeometry")]
public sealed class TextureElementTests
{
    #region Public API
    /// <summary>Replacing owned SVG artwork releases it while borrowed PNG fallback artwork remains engine-owned.</summary>
    [Fact]
    public void SvgTexturesAreDisposedAndPngFallbackTexturesRemainBorrowed()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        const string svgPath = "textures/hud/weather/composite/rain-light.svg";
        const string pngPath = "textures/hud/weather/rain.png";
        var svgTexture = new LoadedTexture(fixture.Api.Object);
        fixture.Gui.Setup(gui => gui.LoadSvg(It.IsAny<AssetLocation>(), 64, 64, 64, 64, It.IsAny<int?>()))
            .Returns(svgTexture);
        LoadedTexture? pngTexture = null;
        var pngPaths = new List<string>();
        fixture.Render.Setup(renderer => renderer.GetOrLoadTexture(It.IsAny<AssetLocation>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new TextureLoad((AssetLocation location, ref LoadedTexture texture) =>
            {
                pngPaths.Add(location.Path);
                pngTexture = texture;
            }));
        var element = new TextureElement(fixture.Api.Object, ElementBounds.Fixed(0, 0, 64, 64), fixture.Failures.Add);

        // Act
        element.Prepare(fixture.Context, svgPath);
        element.Prepare(fixture.Context, svgPath);

        // Assert
        Assert.False(svgTexture.Disposed);

        // Act
        element.Prepare(fixture.Context, pngPath);

        // Assert
        Assert.True(svgTexture.Disposed);
        Assert.Equal([pngPath], pngPaths);
        Assert.NotNull(pngTexture);
        Assert.True(pngTexture!.IgnoreUndisposed);

        // Act
        element.Dispose();
        element.Dispose();

        // Assert
        Assert.False(pngTexture.Disposed);
        Assert.Empty(fixture.Failures);
        fixture.Gui.Verify(gui => gui.LoadSvg(
            It.Is<AssetLocation>(location => location.Path == svgPath), 64, 64, 64, 64, It.IsAny<int?>()), Times.Once);
    }
    #endregion

    #region Private
    /// <summary>Matches the texture cache API's ref-based native loading contract.</summary>
    private delegate void TextureLoad(AssetLocation location, ref LoadedTexture texture);
    #endregion
}
