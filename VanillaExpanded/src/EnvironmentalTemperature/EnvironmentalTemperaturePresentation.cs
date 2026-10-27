using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Retains local climate readings and calibration independently of native visual resources.</summary>
internal sealed class EnvironmentalTemperaturePresentation : InstrumentPresentation
{
    private readonly Func<EnvironmentalTemperatureLiquidMask>? createLiquid;
    private readonly Func<HudOverlayPreparationContext, LoadedTexture>? createTicks;

    #region Public API
    /// <summary>Gets the bounded visual fill while leaving the numeric reading unclamped.</summary>
    internal float MarkerPosition => EnvironmentalTemperatureGaugeLayout.Fill(Temperature);
    /// <summary>Gets the sampled climate temperature.</summary>
    internal float Temperature { get; private set; }
    /// <summary>Gets the prepared whole-degree localized reading.</summary>
    internal string Reading { get; private set; } = string.Empty;
    /// <summary>Accepts per-element glyph, borrowed artwork, liquid and generated-scale factories.</summary>
    public EnvironmentalTemperaturePresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, LoadedTexture>? createTicks = null,
        Func<EnvironmentalTemperatureLiquidMask>? createLiquid = null) : base(measure, createText, load)
    {
        this.createTicks = createTicks; this.createLiquid = createLiquid;
    }
    /// <summary>Stores the sampled local Celsius reading.</summary>
    public void SetContent(float celsius) => Temperature = celsius;
    /// <summary>Localizes readings and publishes dimensions without allocating textures.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        double rounded = Math.Round(Temperature, MidpointRounding.AwayFromZero);
        Reading = Lang.GetL(context.Locale, "vanillaexpanded:environmental-temperature-reading", rounded == 0 ? 0d : rounded);
        if (PrepareSimple(context, "temperature", Reading, "textures/hud/environmental-temperature/icon.svg")) return;
        Size = new(HudOverlaySizes.EnvironmentalThermometer, HudOverlaySizes.EnvironmentalThermometer); Visibility.Show();
    }
    /// <summary>Creates separate native mechanism, scale, lettering and housing elements.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == EnvironmentalGaugeStyle.Immersive
        ? new ClimateView(api, this, Load, CreateText, createTicks, createLiquid) : CreateSimpleView(api);
    #endregion

    #region Protected API
    /// <summary>Clears climate data without touching composer-owned resources.</summary>
    protected override void ClearContent() { Temperature = 0; Reading = string.Empty; }
    #endregion
}
