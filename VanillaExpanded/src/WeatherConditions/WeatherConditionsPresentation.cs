using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using AssetLocation = Vintagestory.API.Common.AssetLocation;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Prepares independent weather layer identities and metrics without owning GUI textures.</summary>
internal sealed class WeatherConditionsPresentation : IndicatorPresentation
{
    private WeatherConditionsSample? sample;
    private ICoreClientAPI? packApi;
    private bool? complete;
    private readonly Func<HudOverlayPreparationContext, bool> hasCompletePack;
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding>? load;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;

    #region Public API
    /// <summary>Gets the selected ordered layers with local GUI-unit geometry.</summary>
    public WeatherLayer[] Layers { get; private set; } = [];
    /// <summary>Gets the borrowed procedural circle service.</summary>
    public IHudOverlayIconBackdrop? Backdrop { get; private set; }
    /// <summary>Accepts CPU measurements, pack availability and per-element resource boundaries.</summary>
    public WeatherConditionsPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        Func<HudOverlayPreparationContext, bool>? hasCompletePack = null) : base(measure: measure, createText: createText)
    {
        this.load = load; this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
        this.hasCompletePack = hasCompletePack ?? CompletePack;
    }
    /// <summary>Retains immutable sampled weather channels independently of native elements.</summary>
    public void SetContent(WeatherConditionsSample value) => sample = value;
    /// <summary>Selects a complete composite or the precipitation/fog fallback before native composition.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (sample == null) return;
        bool hasData = sample.Precipitation.HasValue || sample.Fog || sample.CloudCoverage.HasValue || sample.ThunderIntensity > 0;
        if (Style == HudIndicatorStyle.Text)
        {
            if (hasData) PrepareText(context, "weather", WeatherConditionsText.Format(sample));
            else Size = SizeF.Empty;
            return;
        }
        if (!ReferenceEquals(packApi, context.Api)) { packApi = context.Api; complete = null; Backdrop = null; }
        // Choose one compatible pack before a view can request textures; never mix partial composite artwork.
        if (hasData) complete ??= hasCompletePack(context);
        bool visible = hasData && (complete == true || sample.Precipitation.HasValue || sample.Fog);
        Size = visible ? new(HudOverlaySizes.Standard, HudOverlaySizes.Standard) : SizeF.Empty;
        if (!visible) { Layers = []; return; }
        if (complete == true)
        {
            int[] selected = WeatherConditionsLayers.Select(sample);
            Layers = new WeatherLayer[selected.Length];
            for (int index = 0; index < selected.Length; index++)
                Layers[index] = new("textures/hud/weather/composite/" + WeatherConditionsLayers.Names[selected[index]] + ".png",
                    new(0, 0, Size.Width, Size.Height));
        }
        else
        {
            // The fallback splits the native slot only when precipitation and fog must coexist.
            bool both = sample.Precipitation.HasValue && sample.Fog;
            float side = both ? Size.Width / 2 : Size.Width, top = both ? Size.Height / 4 : 0;
            var layers = new System.Collections.Generic.List<WeatherLayer>(2);
            if (sample.Precipitation is EnumPrecipitationType type)
            {
                string name = type switch { EnumPrecipitationType.Snow => "snow", EnumPrecipitationType.Hail => "hail", _ => "rain" };
                layers.Add(new("textures/hud/weather/" + name + ".png", new(0, top, side, side)));
            }
            if (sample.Fog) layers.Add(new("textures/hud/weather/fog.png", new(both ? side : 0, top, side, side)));
            Layers = layers.ToArray();
        }
        Backdrop = loadBackdrop(context); Visibility.Show();
    }
    /// <summary>Creates native text or distinct borrowed weather-layer elements for one composer.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api) : new WeatherView(api, this, load);
    #endregion

    #region Protected API
    /// <summary>Clears sampled data and asset-pack availability at completed exit or world change.</summary>
    protected override void ClearContent() { sample = null; Layers = []; Backdrop = null; complete = null; packApi = null; }
    #endregion

    #region Private
    /// <summary>Checks the complete full-canvas pack before requesting any of its textures.</summary>
    private static bool CompletePack(HudOverlayPreparationContext context)
    {
        foreach (string name in WeatherConditionsLayers.Names)
            if (!context.Api.Assets.Exists(new AssetLocation(Constants.ModId, "textures/hud/weather/composite/" + name + ".png"))) return false;
        return true;
    }
    #endregion
}
