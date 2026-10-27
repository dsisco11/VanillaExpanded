using System;
using System.Drawing;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Retains sampled ammunition and localized metrics independently of composer-owned resources.</summary>
internal sealed class BowAmmunitionPresentation : IndicatorPresentation
{
    private BowAmmunitionSample? sample;
    private readonly System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createBackground;

    #region Public API
    /// <summary>Gets the stable sampled arrow; each native element makes its own rendering copy.</summary>
    public ItemStack? Icon => sample?.Icon;
    /// <summary>Gets the localized prepared count or empty-state text.</summary>
    public string Reading { get; private set; } = string.Empty;
    /// <summary>Gets canonical lettering dimensions without generating duplicate glyphs.</summary>
    public SizeF ReadingSize { get; private set; }
    /// <summary>Accepts measurement and upload boundaries for headless verification.</summary>
    public BowAmmunitionPresentation(System.Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        System.Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        System.Func<HudOverlayPreparationContext, float, LoadedTexture>? createBackground = null) : base(measure: measure, createText: createText)
    {
        this.createBackground = createBackground;
    }
    /// <summary>Retains an already-copied selection snapshot outside rendering.</summary>
    public void SetContent(BowAmmunitionSample value) => sample = value;
    /// <summary>Prepares localized counts and dimensions while leaving GPU ownership to elements.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (sample == null) return;
        if (Style == HudIndicatorStyle.Text)
        {
            PrepareText(context, "ammunition", sample.Quantity.ToString(CultureInfo.InvariantCulture)); return;
        }
        Reading = sample.Icon == null ? Lang.GetL(context.Locale, "vanillaexpanded:bow-ammunition-empty")
            : Lang.GetL(context.Locale, "vanillaexpanded:bow-ammunition-count", sample.Quantity);
        ReadingSize = MeasureText(context, Reading, 1.5);
        Size = sample.Icon == null ? ReadingSize : new(Math.Max(HudOverlaySizes.Small, ReadingSize.Width),
            ReadingSize.Height + HudOverlaySizes.ReadingGap + HudOverlaySizes.Small);
        Visibility.Show();
    }
    /// <summary>Creates unique native lettering and item components for the requested composition.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api)
        : new ArrowView(api, this, CreateArrowText, createBackground);
    #endregion

    #region Protected API
    /// <summary>Clears prepared content after the retained exit completes.</summary>
    protected override void ClearContent() { sample = null; Reading = string.Empty; ReadingSize = SizeF.Empty; }
    #endregion

    #region Private
    /// <summary>Preserves muted empty-state contrast while native elements retain glyph ownership.</summary>
    private LoadedTexture CreateArrowText(HudOverlayPreparationContext context, string text, CairoFont font)
    {
        font.StrokeWidth = 1.5;
        font.WithColor(Icon == null ? new double[] { .7, .7, .7, 1 } : new double[] { 1, 1, 1, 1 });
        if (CreateText != null) return CreateText(context, text, font);
        var texture = context.Api.Gui.TextTexture.GenTextTexture(text, font);
        try { return HudOverlayTextureFiltering.Prepare(texture); }
        catch { texture.Dispose(); throw; }
    }
    #endregion
}
