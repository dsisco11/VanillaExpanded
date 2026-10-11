using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
namespace VanillaExpanded.Tests.Unit.HudOverlays;
/// <summary>Checks text resource preparation caching and item-copy ownership without uploading GPU resources.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayIconTextPresentationTests : IDisposable
{
    private readonly ITranslationService? english = Vintagestory.API.Config.Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    #region Lifecycle
    /// <summary>Provides borrowed native translation service state for headless preparation.</summary>
    public HudOverlayIconTextPresentationTests()
    {
        var translations = new Mock<ITranslationService>();
        translations.Setup(x => x.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] args) => key == "vanillaexpanded:bow-ammunition-count" ? $"{args[0]}x" : key);
        Vintagestory.API.Config.Lang.AvailableLanguages["en"] = translations.Object;
    }
    /// <summary>Restores the engine translation registry after preparation checks.</summary>
    public void Dispose()
    {
        if (english == null) Vintagestory.API.Config.Lang.AvailableLanguages.Remove("en");
        else Vintagestory.API.Config.Lang.AvailableLanguages["en"] = english;
    }
    #endregion
    #region Presentation
    /// <summary>Unchanged contexts reuse text while relevant locale, font, scale and content changes rebuild once.</summary>
    [Fact]
    public void PreparationCachesNativeTextAndDrawUsesCopiedStack()
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        var renderer = new Mock<IRenderAPI>();
        api.SetupGet(x => x.Gui).Returns(gui.Object);
        api.SetupGet(x => x.Render).Returns(renderer.Object);
        int uploads = 0;
        var textures = new List<LoadedTexture>();
        var live = new ItemStack(new Item { Code = new AssetLocation("game:arrow-flint") }, 20);
        using var presentation = new HudOverlayIconTextPresentation((_, localized) =>
        {
            uploads++;
            var texture = new LoadedTexture(api.Object) { Width = 20, Height = 14 };
            textures.Add(texture);
            return texture;
        });
        presentation.SetContent(live, "20");
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.Prepare(context);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 1, "en"));
        presentation.SetContent(live, "20");
        presentation.Prepare(context);
        Assert.Equal(1, uploads);
        live.StackSize = 3;
        ItemStack? drawn = null;
        renderer.Setup(x => x.RenderItemstackToGui(It.IsAny<ItemSlot>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<float>(), It.IsAny<int>(), It.IsAny<float>(), true, false, false)).Callback<ItemSlot, double, double, double, float, int, float, bool, bool, bool>(
                (slot, _, _, _, _, _, _, _, _, _) => drawn = slot.Itemstack);
        var bounds = ElementBounds.Fixed(0, 0, 80, 40).WithEmptyParent();
        bounds.CalcWorldBounds();
        presentation.Draw(renderer.Object, bounds, new RectangleF(0, 0, 80, 40), .1f);
        Assert.NotSame(live, drawn);
        Assert.Equal(20, drawn!.StackSize);
        Assert.Equal(1, uploads);
        presentation.Prepare(context with { });
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 1, "de"));
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 1, "de", 1));
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 2, "de", 1));
        Assert.Equal(4, uploads);
        presentation.SetContent(null, "0");
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 2, "de", 1));
        Assert.Equal(5, uploads);
        Assert.All(textures.Take(4), texture => Assert.True(texture.Disposed));
        Assert.False(textures.Last().Disposed);
        presentation.Reset();
        presentation.Reset();
        Assert.Equal(SizeF.Empty, presentation.Size);
        Assert.All(textures, texture => Assert.True(texture.Disposed));
    }
    /// <summary>Compact count-first content keeps native pixel widths and centers its icon at both GUI scales.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CompactPresentationPlacesCountBeforeIcon(double scale)
    {
        var api = new Mock<ICoreClientAPI>();
        var renderer = new Mock<IRenderAPI>();
        api.SetupGet(x => x.Render).Returns(renderer.Object);
        using var presentation = new HudOverlayIconTextPresentation((_, localized) =>
        {
            Assert.Equal("20x", localized);
            return new LoadedTexture(api.Object) { Width = (int)(20 * scale), Height = (int)(14 * scale) };
        }, iconSize: 24, textBeforeIcon: true, gap: 4, fontSize: 14);
        presentation.SetContent(new ItemStack(new Item { Code = new AssetLocation("game:arrow-flint") }), "vanillaexpanded:bow-ammunition-count", 20);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, scale, "en"));
        Assert.Equal(new SizeF(48, 24), presentation.Size);
        // Set native resolved pixels directly so the test does not mutate global GUI scale.
        var bounds = ElementBounds.Fixed(0, 0, 48, 24).WithEmptyParent();
        bounds.absFixedX = 10;
        bounds.absFixedY = 20;
        bounds.absInnerWidth = 48 * scale;
        bounds.absInnerHeight = 24 * scale;
        presentation.Draw(renderer.Object, bounds, new RectangleF(10, 20, (float)(48 * scale), (float)(24 * scale)), .1f);
        renderer.Verify(x => x.Render2DTexturePremultipliedAlpha(0, 10, 20 + 5 * scale, 20 * scale, 14 * scale, 50), Times.Once);
        renderer.Verify(x => x.RenderItemstackToGui(It.IsAny<ItemSlot>(), 10 + 36 * scale, 20 + 12 * scale, 50,
            (float)(24 * scale), -1, .1f, true, false, false), Times.Once);
    }
    /// <summary>The icon alone receives a scale-aware cached backdrop behind its native item draw.</summary>
    [Fact]
    public void CircularBackgroundCachesAcrossContentChangesAndDisposesOnReset()
    {
        var api = new Mock<ICoreClientAPI>();
        var renderer = new Mock<IRenderAPI>();
        api.SetupGet(x => x.Render).Returns(renderer.Object);
        api.SetupGet(x => x.Gui).Returns(new Mock<IGuiAPI>().Object);
        var backgrounds = new List<LoadedTexture>();
        using var presentation = new HudOverlayIconTextPresentation((_, _) => new LoadedTexture(api.Object) { Width = 20, Height = 14 },
            iconSize: 24, textBeforeIcon: true, gap: 4, fontSize: 14, circularIconBackground: true,
            createIconBackground: (preparation, diameter) =>
            {
                Assert.Equal(24, diameter);
                var texture = new LoadedTexture(api.Object) { Width = (int)(diameter * preparation.GuiScale), Height = (int)(diameter * preparation.GuiScale), TextureId = 42 + backgrounds.Count };
                backgrounds.Add(texture);
                return texture;
            });
        var stack = new ItemStack(new Item { Code = new AssetLocation("game:arrow-flint") });
        presentation.SetContent(stack, "vanillaexpanded:bow-ammunition-count", 20);
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.Prepare(context);
        presentation.SetContent(stack, "vanillaexpanded:bow-ammunition-count", 19);
        presentation.Prepare(context);
        Assert.Single(backgrounds);
        Assert.Equal(new SizeF(48, 24), presentation.Size);
        var bounds = ElementBounds.Fixed(0, 0, 48, 24).WithEmptyParent();
        bounds.absFixedX = 10; bounds.absFixedY = 20;
        bounds.absInnerWidth = 48; bounds.absInnerHeight = 24;
        var draws = new List<string>();
        renderer.Setup(x => x.Render2DTexturePremultipliedAlpha(42, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), 49)).Callback<int, double, double, double, double, float, Vintagestory.API.MathTools.Vec4f>((_, x, y, width, height, _, _) =>
        {
            Assert.Equal(34, x); Assert.Equal(20, y);
            Assert.Equal(24, width); Assert.Equal(24, height);
            draws.Add("circle");
        });
        renderer.Setup(x => x.RenderItemstackToGui(It.IsAny<ItemSlot>(), 46, 32, 50, 24, -1, .1f, true, false, false)).Callback(() => draws.Add("icon"));
        presentation.Draw(renderer.Object, bounds, new RectangleF(10, 20, 48, 24), .1f);
        presentation.Draw(renderer.Object, bounds, new RectangleF(10, 20, 48, 24), .1f);
        Assert.Equal(new[] { "circle", "icon", "circle", "icon" }, draws);
        Assert.Single(backgrounds);
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, 2, "en"));
        Assert.Equal(2, backgrounds.Count);
        Assert.True(backgrounds[0].Disposed);
        Assert.Equal(48, backgrounds[1].Width);
        presentation.Reset();
        presentation.Reset();
        Assert.All(backgrounds, texture => Assert.True(texture.Disposed));
    }
    /// <summary>The native Cairo upload contains translucent black central pixels and transparent corners.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void NativeBackgroundComposesBlackCircle(double scale)
    {
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        api.SetupGet(x => x.Gui).Returns(gui.Object);
        int uploads = 0;
        gui.Setup(x => x.LoadOrUpdateCairoTexture(It.IsAny<Cairo.ImageSurface>(), true, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new CairoUpload((Cairo.ImageSurface surface, bool _, ref LoadedTexture texture) =>
            {
                uploads++;
                surface.Flush();
                Assert.Equal((int)(24 * scale), surface.Width);
                Assert.Equal(surface.Width, surface.Height);
                byte[] pixels = surface.Data;
                Assert.Equal(0, pixels[3]);
                int center = (surface.Height / 2 * surface.Stride) + surface.Width / 2 * 4;
                Assert.Equal(0, pixels[center]);
                Assert.Equal(0, pixels[center + 1]);
                Assert.Equal(0, pixels[center + 2]);
                Assert.InRange(pixels[center + 3], (byte)127, (byte)128);
            }));
        using var presentation = new HudOverlayIconTextPresentation((_, _) => new LoadedTexture(api.Object),
            iconSize: 24, circularIconBackground: true);
        presentation.SetContent(new ItemStack(new Item { Code = new AssetLocation("game:arrow-flint") }), "20");
        presentation.Prepare(new HudOverlayPreparationContext(api.Object, scale, "en"));
        Assert.Equal(1, uploads);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Matches the native upload boundary's borrowed Cairo surface and owned texture reference.</summary>
    private delegate void CairoUpload(Cairo.ImageSurface surface, bool linearMag, ref LoadedTexture texture);
    #endregion
}
