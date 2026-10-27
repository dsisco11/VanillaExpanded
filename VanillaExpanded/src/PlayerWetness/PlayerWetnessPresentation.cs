using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Prepares wetness readings and metrics while native elements own the drop and glyph resources.</summary>
internal sealed class PlayerWetnessPresentation : IndicatorPresentation
{
    private int percentage;
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding>? load;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;

    #region Public API
    /// <summary>Gets prepared localized lettering and its fitted GUI-unit width.</summary>
    public string Reading { get; private set; } = string.Empty;
    /// <summary>Gets the width of the reading fitted into its reserved row.</summary>
    public float ReadingWidth { get; private set; }
    /// <summary>Gets the borrowed procedural backdrop service.</summary>
    public IHudOverlayIconBackdrop? Backdrop { get; private set; }
    /// <summary>Accepts CPU measurements and per-element upload boundaries for headless verification.</summary>
    public PlayerWetnessPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null) : base(measure: measure, createText: createText)
    {
        this.load = load; this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Rounds the synchronized wetness while retaining a visible percentage for nonzero samples.</summary>
    public void SetContent(float value) => percentage = Math.Clamp((int)Math.Round(value * 100, MidpointRounding.AwayFromZero), 1, 100);
    /// <summary>Prepares localized data and canonical metrics without allocating glyphs or drop textures.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        Reading = Lang.GetL(context.Locale, "vanillaexpanded:player-wetness-reading", percentage);
        if (Style == HudIndicatorStyle.Text) { PrepareText(context, "wetness", Reading); return; }
        var text = MeasureText(context, Reading, 1.5);
        ReadingWidth = text.Width * Math.Min(1, HudOverlaySizes.WetnessReadingHeight / Math.Max(1, text.Height));
        Size = new(Math.Max(HudOverlaySizes.Standard, ReadingWidth), HudOverlaySizes.Standard);
        Backdrop = loadBackdrop(context); Visibility.Show();
    }
    /// <summary>Creates unique native glyph and drop components for one composer.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api)
        : new WetnessView(api, this, CreateText, load);
    #endregion

    #region Protected API
    /// <summary>Clears retained wetness and prepared data at completed exit or world change.</summary>
    protected override void ClearContent() { percentage = 0; Reading = string.Empty; ReadingWidth = 0; Backdrop = null; }
    #endregion
}
