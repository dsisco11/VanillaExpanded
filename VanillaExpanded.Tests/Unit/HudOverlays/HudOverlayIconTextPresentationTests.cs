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
    #endregion
}
