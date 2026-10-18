using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.RiftActivity;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.RiftActivity;

/// <summary>Checks abbreviation caching, composition geometry, and resource ownership without native fonts.</summary>
[Collection("HudOverlayGeometry")]
public sealed class RiftActivityLabelTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Resolves the abbreviation keys independently of engine language startup.</summary>
    public RiftActivityLabelTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => key.Split('-').Last() switch
            {
                "low" => "LOW", "medium" => "MED", "high" => "HIGH", "apocalyptic" => "APOC", _ => key
            });
        Lang.AvailableLanguages["en"] = language.Object;
    }

    /// <summary>Restores the process-wide language registry after every case.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Each native activity uses a cached abbreviation centered below its matching artwork and backdrop.</summary>
    [Theory]
    [InlineData(0, "LOW", 1, 70, 15, 9, 17, 140, 120)]
    [InlineData(1, "MED", 1.5, 25, 18, 20, 30, 150, 180)]
    [InlineData(2, "HIGH", 2, 100, 25, 1, 2, 240, 200)]
    [InlineData(3, "APOC", 1, 55, 16, 4, 8, 160, 140)]
    public void AbbreviationIsCachedBelowCenteredArtwork(int level, string expected, double scale,
        int textWidth, int textHeight, double x, double y, double width, double height)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var readings = new List<string>();
        var owned = new List<LoadedTexture>();
        using var label = new HudOverlayIconTextPresentation((context, text) =>
        {
            readings.Add(text);
            var result = new LoadedTexture(context.Api) { TextureId = 30 + readings.Count, Width = textWidth, Height = textHeight };
            owned.Add(result); return result;
        });
        var backdrop = new Mock<IHudOverlayIconBackdrop>();
        var artwork = Enumerable.Range(1, 4).Select(id => new LoadedTexture(api.Object) { TextureId = id, IgnoreUndisposed = true }).ToArray();
        using var presentation = new RiftActivityPresentation(_ => artwork, _ => backdrop.Object, label: label);
        var preparation = new HudOverlayPreparationContext(api.Object, scale, "en");
        presentation.SetContent((RiftActivityLevel)level); presentation.Prepare(preparation); presentation.Prepare(preparation);
        Assert.Equal(new[] { expected }, readings);
        var bounds = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
        bounds.absFixedX = x; bounds.absFixedY = y; bounds.absInnerWidth = width; bounds.absInnerHeight = height; bounds.Initialized = true;
        var renderer = new Mock<IRenderAPI>();
        presentation.CollectBackdrops(renderer.Object, bounds); presentation.Draw(renderer.Object, bounds);
        var icon = Assert.Single(renderer.Invocations, value => value.Method.Name == "RenderTexture");
        var textDraw = Assert.Single(renderer.Invocations, value => value.Method.Name == "Render2DTexturePremultipliedAlpha");
        double iconX = (double)icon.Arguments[1], iconY = (double)icon.Arguments[2];
        double iconWidth = (double)icon.Arguments[3], iconHeight = (double)icon.Arguments[4];
        double textX = (double)textDraw.Arguments[1], textY = (double)textDraw.Arguments[2];
        Assert.Equal(x + width / 2, iconX + iconWidth / 2, 5);
        Assert.Equal(iconX + iconWidth / 2, textX + textWidth / 2d, 5);
        Assert.True(textY >= iconY + iconHeight);
        Assert.True(textY + textHeight <= y + height);
        var queued = Assert.Single(backdrop.Invocations);
        Assert.Equal(iconX, queued.Arguments[1]); Assert.Equal(iconY, queued.Arguments[2]);
        Assert.Equal((int)level + 1, icon.Arguments[0]);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, scale, "en", 1));
        Assert.Equal(2, readings.Count); Assert.Equal(0, owned[0].TextureId);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, scale + .5, "en"));
        Assert.Equal(3, readings.Count); Assert.Equal(0, owned[1].TextureId);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, scale, "missing-locale"));
        Assert.Equal(4, readings.Count); Assert.Equal(0, owned[2].TextureId);
        presentation.Reset();
        Assert.Equal(0, owned[3].TextureId); Assert.Equal(SizeF.Empty, presentation.Size);
        Assert.All(artwork, texture => Assert.NotEqual(0, texture.TextureId));
    }
    #endregion
}
