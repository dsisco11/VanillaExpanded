using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks shared presentation style, content retention, and ownership independently of individual instruments.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudIndicatorPresentationTests : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Provides a fixed short label without native language initialization.</summary>
    public HudIndicatorPresentationTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("TEST");
        Lang.AvailableLanguages["en"] = language.Object;
    }

    /// <summary>Restores the shared language registry after each base contract fixture.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>The two-mode compact enum accepts defined modes and falls back to its icon default.</summary>
    [Fact]
    public void CompactEnumUsesItsOwnDefaultForInvalidValues()
    {
        using var presentation = new Probe<HudIndicatorStyle>(HudIndicatorStyle.Icon, HudIndicatorStyle.Text);
        Assert.Equal(HudIndicatorStyle.Icon, presentation.CurrentStyle);
        Assert.False(presentation.SetStyle((HudIndicatorStyle)99));
        Assert.True(presentation.SetStyle(HudIndicatorStyle.Text));
        Assert.True(presentation.SetStyle((HudIndicatorStyle)(-1)));
        Assert.Equal(HudIndicatorStyle.Icon, presentation.CurrentStyle);
    }

    /// <summary>The environmental enum retains its immersive default while accepting both icon and text choices.</summary>
    [Fact]
    public void EnvironmentalEnumRetainsAllThreeDefinedModes()
    {
        using var presentation = new Probe<EnvironmentalGaugeStyle>(EnvironmentalGaugeStyle.Immersive, EnvironmentalGaugeStyle.Text);
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, presentation.CurrentStyle);
        Assert.True(presentation.SetStyle(EnvironmentalGaugeStyle.Icon));
        Assert.True(presentation.SetStyle(EnvironmentalGaugeStyle.Text));
        Assert.True(presentation.SetStyle((EnvironmentalGaugeStyle)99));
        Assert.Equal(EnvironmentalGaugeStyle.Immersive, presentation.CurrentStyle);
    }

    /// <summary>Style changes release old resources while preserving sampled contents, feature timing, and the transition object.</summary>
    [Fact]
    public void StyleChangePreservesContentAndCachesPreparedText()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        var ownedText = new List<LoadedTexture>();
        using var presentation = new Probe<HudIndicatorStyle>(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, CreateText(ownedText));
        presentation.SetContent("12°C", 73);
        presentation.Prepare(context);
        var feature = presentation.Artwork!;
        var visibility = presentation.Visibility;
        Assert.True(presentation.SetStyle(HudIndicatorStyle.Text));
        Assert.True(feature.Disposed); Assert.Equal("12°C", presentation.Content); Assert.Equal(73, presentation.FeatureTimer);
        Assert.Same(visibility, presentation.Visibility); Assert.True(visibility.Renderable);
        Assert.False(presentation.HasContext); Assert.Equal(SizeF.Empty, presentation.Size);
        presentation.Prepare(context); presentation.Prepare(context);
        Assert.Equal(2, ownedText.Count);
        Assert.True(presentation.HasContext); Assert.Equal(new SizeF(54, 36), presentation.Size);
        Assert.False(presentation.SetStyle(HudIndicatorStyle.Text));
        Assert.All(ownedText, texture => Assert.False(texture.Disposed));
        presentation.SetStyle(HudIndicatorStyle.Icon);
        Assert.All(ownedText, texture => Assert.True(texture.Disposed));
        Assert.Equal("12°C", presentation.Content); Assert.Equal(73, presentation.FeatureTimer);
        presentation.Prepare(context);
        Assert.NotSame(feature, presentation.Artwork);
    }

    /// <summary>Immediate reset releases shared and feature ownership, clears sample state, and supports repeated final removal.</summary>
    [Fact]
    public void ResetAndDisposeClearAllOwnership()
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var context = new HudOverlayPreparationContext(api.Object, 1, "en");
        var text = new List<LoadedTexture>();
        var presentation = new Probe<HudIndicatorStyle>(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, CreateText(text));
        presentation.SetContent("sample", 19); presentation.Prepare(context);
        var feature = presentation.Artwork!;
        presentation.Reset();
        Assert.True(feature.Disposed); Assert.Null(presentation.Content); Assert.Equal(0, presentation.FeatureTimer);
        Assert.False(presentation.HasContext); Assert.False(presentation.Visibility.Renderable); Assert.Equal(SizeF.Empty, presentation.Size);
        presentation.SetContent("text", 20); presentation.SetStyle(HudIndicatorStyle.Text); presentation.Prepare(context);
        presentation.Dispose(); presentation.Dispose();
        Assert.All(text, value => Assert.True(value.Disposed)); Assert.Null(presentation.Content);
    }

    /// <summary>Ordinary fade and flashing exits defer resource and content release until their distinct durations finish.</summary>
    [Theory]
    [InlineData(false, 250)] [InlineData(true, 1000)]
    public void AnimatedExitRetainsContentUntilCompletion(bool flash, int duration)
    {
        long now = 0;
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using var presentation = new Probe<HudIndicatorStyle>(HudIndicatorStyle.Icon, HudIndicatorStyle.Text,
            flash: flash, clock: () => now);
        presentation.Visibility.Enable();
        presentation.SetContent("sample", 47); presentation.Prepare(new(api.Object, 1, "en"));
        now = duration;
        Assert.Equal(1, presentation.Visibility.Opacity);
        var artwork = presentation.Artwork!;
        presentation.Hide();
        now += duration - 1;
        Assert.True(presentation.Visibility.Exiting); Assert.True(presentation.Visibility.Renderable);
        Assert.Equal("sample", presentation.Content); Assert.Equal(47, presentation.FeatureTimer); Assert.False(artwork.Disposed);
        now++;
        Assert.False(presentation.Visibility.Renderable);
        Assert.True(artwork.Disposed); Assert.Null(presentation.Content); Assert.Equal(0, presentation.FeatureTimer);
        Assert.Equal(SizeF.Empty, presentation.Size);
    }

    /// <summary>A style change keeps an in-progress exit instead of resetting or replacing its content-clear deadline.</summary>
    [Fact]
    public void StyleChangeDoesNotCancelAnActiveExit()
    {
        long now = 0;
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using var presentation = new Probe<HudIndicatorStyle>(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, clock: () => now);
        presentation.Visibility.Enable(); presentation.SetContent("retained", 9); presentation.Prepare(new(api.Object, 1, "en"));
        now = 250; presentation.Hide(); now = 300;
        presentation.SetStyle(HudIndicatorStyle.Text);
        Assert.True(presentation.Visibility.Exiting); Assert.Equal("retained", presentation.Content);
        now = 500;
        Assert.False(presentation.Visibility.Renderable); Assert.Null(presentation.Content);
    }
    #endregion

    #region Private
    /// <summary>Records each shared text allocation with the same native ownership contract as instrument lettering.</summary>
    private static HudIndicatorTextPresentation CreateText(List<LoadedTexture> textures)
    {
        LoadedTexture Create(HudOverlayPreparationContext context, string content)
        {
            var texture = new LoadedTexture(context.Api) { TextureId = textures.Count + 1, Width = content.Length * 7, Height = 14 };
            textures.Add(texture); return texture;
        }
        return new(new WorkshopGaugeText(Create), new WorkshopGaugeText(Create));
    }

    /// <summary>Provides independently owned artwork and sample timing to exercise the base ownership boundary.</summary>
    private sealed class Probe<TStyle> : HudIndicatorPresentation<TStyle> where TStyle : struct, Enum
    {
        /// <summary>Gets the selected enum mode for contract assertions.</summary>
        public TStyle CurrentStyle => Style;
        /// <summary>Gets whether drawing resources have a prepared native context.</summary>
        public bool HasContext => Context != null;
        /// <summary>Gets retained sample data, which must survive changes of presentation mode.</summary>
        public string? Content { get; private set; }
        /// <summary>Gets a feature-specific timer that is independent of presentation resources.</summary>
        public int FeatureTimer { get; private set; }
        /// <summary>Gets the independently owned feature artwork.</summary>
        public LoadedTexture? Artwork { get; private set; }

        #region Public API
        /// <summary>Chooses a concrete mode pair and deterministic animation clock.</summary>
        public Probe(TStyle defaultStyle, TStyle textStyle, HudIndicatorTextPresentation? text = null, bool flash = false, Func<long>? clock = null)
            : base(defaultStyle, textStyle, text, flash, clock) { }

        /// <summary>Sets sample data and a timer without preparing any presentation resources.</summary>
        public void SetContent(string content, int timer) { Content = content; FeatureTimer = timer; }

        /// <summary>Prepares either shared text or independent artwork while preserving the same retained content.</summary>
        public void Prepare(HudOverlayPreparationContext context)
        {
            if (TryPrepareText(context, "test", Content)) return;
            Artwork ??= new LoadedTexture(context.Api) { TextureId = 20 };
            Context = context; Size = new(36, 36); Visibility.Show();
        }
        #endregion

        #region Protected API
        /// <summary>Releases owned feature pixels without touching sample data or feature timing.</summary>
        protected override void ReleaseArtwork() { Artwork?.Dispose(); Artwork = null; }
        /// <summary>Clears sample data and timing after a completed exit or explicit reset.</summary>
        protected override void ClearContent() { Content = null; FeatureTimer = 0; }
        #endregion
    }
    #endregion
}
