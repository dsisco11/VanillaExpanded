using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Composes borrowed weather layers into one fixed HUD footprint with one shared backdrop.</summary>
internal sealed class WeatherConditionsPresentation : IDisposable
{
    private const float Diameter = 24;
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private HudOverlayPreparationContext? context;
    private WeatherConditionsSample? sample;
    private int[] layers = [];
    /// <summary>Controls visibility and retained resources during transitions.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new();
    public SizeF Size { get; private set; }
    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached assets, with an injectable loader for headless lifecycle checks.</summary>
    public WeatherConditionsPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
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
        if (!ReferenceEquals(context?.Api, preparation.Api))
        {
            icons = null;
            backdrop = null;
        }
        bool hasData = sample.Precipitation.HasValue || sample.Fog || sample.CloudCoverage.HasValue
            || sample.WindIntensity > 0 || sample.ThunderIntensity > 0;
        if (hasData && icons == null) icons = load(preparation);
        bool visible = hasData && (icons?.Length == WeatherConditionsLayers.Names.Length || sample.Precipitation.HasValue || sample.Fog);
        if (visible && backdrop == null) backdrop = loadBackdrop(preparation);
        context = preparation;
        Size = visible ? new SizeF(Diameter, Diameter) : SizeF.Empty;
        if (visible) Visibility.Show();
    }
    /// <summary>Draws only borrowed prepared textures; the host owns clipping and GUI visibility.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (sample == null || icons == null || context == null || Size.IsEmpty) return;
        double pixels = Diameter * context.GuiScale;
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
        if (sample == null || icons == null || context == null || Size.IsEmpty) return;
        double y = bounds.renderY + (bounds.OuterHeight - 24 * context.GuiScale) / 2;
        backdrop?.Queue(renderer, bounds.renderX, y, context.GuiScale);
    }
    /// <summary>Retains prepared content until the visibility exit finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Drops bindings without deleting resources owned by the engine cache.</summary>
    public void Reset()
    {
        Visibility.Reset();
        icons = null;
        backdrop = null;
        context = null;
        sample = null;
        layers = [];
        Size = SizeF.Empty;
    }
    /// <summary>Releases borrowed bindings on final removal.</summary>
    public void Dispose() => Reset();
    #endregion
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
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion
}
