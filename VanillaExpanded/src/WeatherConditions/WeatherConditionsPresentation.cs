using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Composes borrowed weather layers into one fixed HUD footprint with one shared backdrop.</summary>
internal sealed class WeatherConditionsPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private const float Diameter = HudOverlaySizes.Standard;
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private WeatherConditionsSample? sample;
    private int[] layers = [];
    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached assets, with an injectable loader for headless lifecycle checks.</summary>
    public WeatherConditionsPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudIndicatorTextPresentation? textStyle = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, textStyle)
    {
        this.load = load ?? LoadIcons;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Stores the immutable visible conditions for the next preparation.</summary>
    public void SetContent(WeatherConditionsSample value)
    {
        sample = value;
        layers = WeatherConditionsLayers.Select(value);
    }
    /// <summary>Loads artwork once per API binding and measures active icons in unscaled GUI units.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (sample == null) return;
        // An empty update can still cross API ownership; discard old cache bindings before deferring the loads.
        if (!ReferenceEquals(Context?.Api, preparation.Api))
        {
            icons = null;
            backdrop = null;
        }
        bool hasData = sample.Precipitation.HasValue || sample.Fog || sample.CloudCoverage.HasValue
            || sample.WindIntensity > 0 || sample.ThunderIntensity > 0;
        if (IsTextStyle)
        {
            if (hasData)
            {
                TryPrepareText(preparation, "weather", WeatherConditionsText.Format(sample));
            }
            else Size = SizeF.Empty;
            return;
        }
        if (hasData && icons == null) icons = load(preparation);
        bool visible = hasData && (icons?.Length == WeatherConditionsLayers.Names.Length || sample.Precipitation.HasValue || sample.Fog);
        if (visible && backdrop == null) backdrop = loadBackdrop(preparation);
        Context = preparation;
        Size = visible ? new SizeF(Diameter, Diameter) : SizeF.Empty;
        if (visible) Visibility.Show();
    }
    /// <summary>Draws only borrowed prepared textures; the host owns clipping and GUI visibility.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (sample == null || Context == null || Size.IsEmpty) return;
        if (TryDrawText(renderer, bounds)) return;
        if (icons == null) return;
        double pixels = Diameter * Context.GuiScale;
        double x = bounds.renderX, y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        if (icons.Length == WeatherConditionsLayers.Names.Length)
        {
            foreach (int layer in layers) renderer.RenderTexture(icons[layer].TextureId, x, y, pixels, pixels, 50, HudOverlayDrawOpacity.Tint);
            return;
        }
        // Until the complete approved layer set exists, retain both existing weather symbols inside the one slot.
        bool both = sample.Precipitation.HasValue && sample.Fog;
        double legacySize = both ? pixels / 2 : pixels;
        double legacyY = both ? y + pixels / 4 : y;
        if (sample.Precipitation is EnumPrecipitationType type)
        {
            int index = type switch { EnumPrecipitationType.Snow => 1, EnumPrecipitationType.Hail => 2, _ => 0 };
            renderer.RenderTexture(icons[index].TextureId, x, legacyY, legacySize, legacySize, 50, HudOverlayDrawOpacity.Tint);
        }
        if (sample.Fog)
        {
            double fogX = both ? x + legacySize : x;
            renderer.RenderTexture(icons[3].TextureId, fogX, legacyY, legacySize, legacySize, 50, HudOverlayDrawOpacity.Tint);
        }
    }
    #endregion
    #region Lifetime
    /// <summary>Queues one shared backdrop only when the prepared composite is visible.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (sample == null || icons == null || Context == null || Size.IsEmpty) return;
        double y = bounds.renderY + (bounds.OuterHeight - HudOverlaySizes.Standard * Context.GuiScale) / 2;
        backdrop?.Queue(renderer, bounds.renderX, y, Context.GuiScale);
    }
    #endregion
    #endregion
    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        icons = null; backdrop = null;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        sample = null; layers = [];
    }
    #endregion

    #region Private
    /// <summary>Loads straight-alpha asset textures while leaving disposal ownership with the game.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        string[] names = WeatherConditionsLayers.Names;
        string folder = "textures/hud/weather/composite/";
        // A partial or absent art pack must not request missing textures or mix incompatible full-canvas artwork.
        foreach (string name in names)
        {
            if (preparation.Api.Assets.Exists(new AssetLocation(Constants.ModId, folder + name + ".png"))) continue;
            names = ["rain", "snow", "hail", "fog"];
            folder = "textures/hud/weather/";
            break;
        }
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                folder + names[index] + ".png"), ref texture);
            HudOverlayTextureFiltering.Prepare(texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion

}
