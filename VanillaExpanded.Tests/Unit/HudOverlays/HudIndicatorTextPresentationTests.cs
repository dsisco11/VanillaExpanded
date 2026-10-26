using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks shared text caching, normal-size layout, fade propagation, and owned texture cleanup.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudIndicatorTextPresentationTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Provides a stable short name for headless text layout tests.</summary>
    public HudIndicatorTextPresentationTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("TEST");
        Lang.AvailableLanguages["en"] = language.Object;
    }

    /// <summary>Restores the borrowed language registry.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Content and preparation context changes replace owned pixels while identical samples reuse them.</summary>
    [Fact]
    public void CachesContentAndRebuildsForContextThenDisposes()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var textures = new List<LoadedTexture>();
        using var presentation = CreatePresentation(textures);
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        presentation.Prepare(context, "test", "first");
        presentation.Prepare(new(api.Object, 1, "en"), "test", "first");
        Assert.Equal(2, textures.Count);
        presentation.Prepare(context, "test", "second");
        Assert.Equal(3, textures.Count); Assert.True(textures[1].Disposed);
        presentation.Prepare(new(api.Object, 2, "en", 1), "test", "second");
        Assert.Equal(5, textures.Count); Assert.True(textures[0].Disposed); Assert.True(textures[2].Disposed);
        presentation.Reset();
        Assert.Equal(SizeF.Empty, presentation.Size); Assert.All(textures, texture => Assert.True(texture.Disposed));
    }

    /// <summary>Long readings remain drawable across GUI scales and reset releases their cached resources.</summary>
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void ScalingAndResetPreserveWholeReadings(double scale)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using var presentation = CreatePresentation(new());
        const string value = "long reading retains every character";
        presentation.Prepare(new(api.Object, scale, "en"), "test", value);
        Assert.True(presentation.Size.Width >= value.Length * 10);
        long now = 0;
        var transition = new HudOverlayVisibilityTransition(clockMs: () => now);
        transition.Enable(); transition.Show(); now = 125;
        var animated = new Mock<IHudOverlayVisibilityTransition>();
        animated.SetupGet(value => value.Visibility).Returns(transition);
        var bounds = ElementBounds.Fixed(0, 0, presentation.Size.Width, 36).WithEmptyParent();
        bounds.absInnerWidth = presentation.Size.Width * scale; bounds.absInnerHeight = 36 * scale; bounds.Initialized = true;
        var renderer = new Mock<IRenderAPI>();
        using (new DrawOpacity(animated.Object)) presentation.Draw(renderer.Object, bounds, .4f);
        Assert.Equal(2, renderer.Invocations.Count);
        foreach (var call in renderer.Invocations)
        {
            Assert.Equal("Render2DTexturePremultipliedAlpha", call.Method.Name);
        }
        Assert.Equal(value.Length * 10 * scale, (double)renderer.Invocations[1].Arguments[3]);
        renderer.Invocations.Clear(); presentation.Reset(); presentation.Draw(renderer.Object, bounds);
        Assert.Empty(renderer.Invocations);
    }
    #endregion

    #region Private
    /// <summary>Creates scaled normal-height native stand-ins and records all owned allocations.</summary>
    private static HudIndicatorTextPresentation CreatePresentation(List<LoadedTexture> textures)
    {
        LoadedTexture Create(HudOverlayPreparationContext context, string content)
        {
            var texture = new LoadedTexture(context.Api) { TextureId = textures.Count + 1,
                Width = (int)(content.Length * 10 * context.GuiScale), Height = (int)(14 * context.GuiScale) };
            textures.Add(texture); return texture;
        }
        return new(new WorkshopGaugeText(Create), new WorkshopGaugeText(Create));
    }
    #endregion
}
