using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.WindSpeed;

/// <summary>Prepares wind percentage data for independently owned native text and SVG elements.</summary>
internal sealed class WindSpeedPresentation : IndicatorPresentation
{
    private int percentage;
    private IconReadingContent? content;
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding>? load;

    #region Public API
    /// <summary>Accepts CPU measurement and element resource factories without retaining their resources.</summary>
    public WindSpeedPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null) : base(measure: measure, createText: createText)
    {
        this.load = load;
    }
    /// <summary>Stores the sampled whole wind percentage outside native rendering.</summary>
    public void SetContent(int value) => percentage = value;
    /// <summary>Localizes and measures the reading without allocating element textures.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (percentage <= 0) return;
        string reading = Lang.GetL(context.Locale, "vanillaexpanded:wind-speed-reading", percentage);
        if (Style == HudIndicatorStyle.Text) { PrepareText(context, "wind-speed", reading); return; }
        SizeF text = MeasureText(context, reading, 1.5);
        content = new("textures/hud/wind-speed/icon.svg", new(HudOverlaySizes.Small, HudOverlaySizes.Small),
            reading, HudOverlaySizes.ReadingGap, ReadingSize: text);
        Size = new(Math.Max(text.Width, content.IconSize.Width), text.Height + content.Gap + content.IconSize.Height);
        Visibility.Show();
    }
    /// <summary>Creates unique composer-owned lettering and approved artwork for this visual instance.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api)
        : new IconReadingView(api, () => content ?? throw new InvalidOperationException("Wind content is not prepared."),
            _ => { }, CreateText, load, strokeWidth: 1.5);
    #endregion

    #region Protected API
    /// <summary>Clears sampled wind and prepared data after the exit completes.</summary>
    protected override void ClearContent() { percentage = 0; content = null; }
    #endregion
}
