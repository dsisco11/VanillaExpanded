using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies generated SVG and borrowed PNG texture ownership at the native texture boundary.</summary>
[Trait("Category", "Unit")]
[Collection("HudOverlayGeometry")]
public sealed class TextureElementTests
{
    #region Public API
    /// <summary>Replacing generated SVG artwork releases it while a generic borrowed PNG remains engine-owned.</summary>
    [Fact]
    public void GeneratedSvgTexturesAreDisposedAndGenericPngTexturesRemainBorrowed()
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        const string svgPath = "textures/hud/weather/composite/rain-light.svg";
        const string pngPath = "textures/test/borrowed.png";
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

    /// <summary>Non-square SVG canvas dimensions reach rasterization unchanged and an unchanged path reuses its texture.</summary>
    [Theory]
    [InlineData(56, 96)]
    [InlineData(32, 16)]
    public void SvgRasterizationUsesRequestedCanvasAndCachesUnchangedArtwork(int width, int height)
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();
        const string svgPath = "textures/hud/body-temperature/fill-mask.svg";
        var svgTexture = new LoadedTexture(fixture.Api.Object);
        fixture.Gui.Setup(gui => gui.LoadSvg(
               It.IsAny<AssetLocation>(), width, height, width, height, It.IsAny<int?>()))
            .Returns(svgTexture);
        var element = new TextureElement(fixture.Api.Object, ElementBounds.Fixed(0, 0, 64, 64),
            fixture.Failures.Add, svgSize: new Size(width, height));

        // Act
        element.Prepare(fixture.Context, svgPath);
        element.Prepare(fixture.Context, svgPath);

        // Assert
        fixture.Gui.Verify(gui => gui.LoadSvg(
            It.Is<AssetLocation>(location => location.Path == svgPath),
            width, height, width, height, It.IsAny<int?>()), Times.Once);
        Assert.False(svgTexture.Disposed);
        Assert.Empty(fixture.Failures);

        // Act
        element.Dispose();

        // Assert
        Assert.True(svgTexture.Disposed);
    }

    /// <summary>Each non-positive SVG canvas dimension is rejected before the element can load artwork.</summary>
    [Theory]
    [InlineData(0, 96)]
    [InlineData(-1, 96)]
    [InlineData(56, 0)]
    [InlineData(56, -1)]
    public void SvgRasterizationRejectsNonPositiveCanvasDimensions(int width, int height)
    {
        // Arrange
        using var fixture = new HudNativeComponentFixture();

        // Act
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TextureElement(fixture.Api.Object, ElementBounds.Fixed(0, 0, 64, 64),
               fixture.Failures.Add, svgSize: new Size(width, height)));

        // Assert
        Assert.Equal("svgSize", error.ParamName);
    }
    #endregion

    #region Private
    /// <summary>Matches the texture cache API's ref-based native loading contract.</summary>
    private delegate void TextureLoad(AssetLocation location, ref LoadedTexture texture);
    #endregion
}
