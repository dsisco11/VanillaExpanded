using Cairo;
using Moq;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Qualifies CPU-only canonical measurements against the installed native text boxing operation.</summary>
[Collection("HudOverlayGeometry")]
public sealed class NativeTextMetricsTests
{
    #region Public API
    /// <summary>Native text upload dimensions agree with canonical metrics across scale, stroke and multiline content.</summary>
    [Theory]
    [InlineData(1, 1, "125%")]
    [InlineData(1.5, 1.5, "HIGH")]
    [InlineData(2, 2.25, "Name\nReading")]
    public void MetricsMatchNativeGlyphBox(float scale, double stroke, string text)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        fixture.Gui.Setup(gui => gui.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(), ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                texture.Width = surface.Width; texture.Height = surface.Height;
            }));
        var predicted = TextMetrics.Measure(fixture.Context, text, stroke);
        using var native = new TextTextureUtil(fixture.Api.Object).GenTextTexture(text, TextMetrics.Font(stroke));
        Assert.Equal(native.Width / (double)scale, predicted.Width, 4);
        Assert.Equal(native.Height / (double)scale, predicted.Height, 4);
    }
    #endregion

    #region Private
    /// <summary>Matches the native ref-texture upload seam without creating a graphics context.</summary>
    private delegate void Upload(ImageSurface surface, bool linear, ref LoadedTexture texture);
    #endregion
}
