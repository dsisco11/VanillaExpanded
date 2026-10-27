using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Prepares body-temperature data and native metrics while element resources belong to composers.</summary>
internal sealed class BodyTemperatureThermometerPresentation : IndicatorPresentation
{
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding>? load;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;

    #region Public API
    /// <summary>Gets the retained immutable sample used by every native visual instance.</summary>
    public BodyTemperatureSample? Sample { get; private set; }
    /// <summary>Gets whether the optional Celsius reading is enabled.</summary>
    public bool ShowReading { get; private set; }
    /// <summary>Gets localized Celsius lettering.</summary>
    public string Reading { get; private set; } = string.Empty;
    /// <summary>Gets the fitted reading width in GUI units.</summary>
    public float ReadingWidth { get; private set; }
    /// <summary>Gets the stable reading/marker column width.</summary>
    public float ColumnWidth { get; private set; }
    /// <summary>Gets the borrowed procedural backdrop service.</summary>
    public IHudOverlayIconBackdrop? Backdrop { get; private set; }
    /// <summary>Accepts CPU metrics and per-element resource factories without taking GUI ownership.</summary>
    public BodyTemperatureThermometerPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null) : base(measure: measure, createText: createText)
    {
        this.load = load; this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Updates fill, warning tint and optional reading preference from synchronized data.</summary>
    public void SetContent(BodyTemperatureSample value, bool showReading) { Sample = value; ShowReading = showReading; }
    /// <summary>Prepares localized strings and native font measurements before group packing.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample == null) return;
        Reading = ShowReading ? Lang.GetL(context.Locale, "vanillaexpanded:body-temperature-reading", Sample.DisplayCelsius) : string.Empty;
        if (Style == HudIndicatorStyle.Text)
        {
            string value = (ShowReading ? Reading + " " : "") + Sample.Risk.ToString().ToUpperInvariant();
            if (Sample.Trend != 0) value += Sample.Trend > 0 ? " +" + Sample.Trend : " " + Sample.Trend;
            PrepareText(context, "body-temperature", value); return;
        }
        var text = ShowReading ? MeasureText(context, Reading, 1.5) : SizeF.Empty;
        ReadingWidth = text.Width * Math.Min(1, HudOverlaySizes.BodyReadingHeight / Math.Max(1, text.Height));
        // The column reserves both marker strengths; changing direction or fill does not move the icon slot.
        ColumnWidth = Math.Max(ReadingWidth, Sample.Trend == 0 ? 0 : HudOverlaySizes.ChevronWidth);
        Size = new(Math.Max(HudOverlaySizes.Standard, ColumnWidth),
            HudOverlaySizes.Standard + (ColumnWidth > 0 ? HudOverlaySizes.Standard + HudOverlaySizes.ReadingGap : 0));
        Backdrop = loadBackdrop(context); Visibility.Show();
    }
    /// <summary>Creates independent native text, trend, mask and outline components.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api)
        : new ThermometerView(api, this, CreateText, load);
    #endregion

    #region Protected API
    /// <summary>Clears sampled and prepared data after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        Sample = null; ShowReading = false; Reading = string.Empty; ReadingWidth = ColumnWidth = 0; Backdrop = null;
    }
    #endregion
}
