using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Borrows cached condition artwork and lays out precipitation and fog as independent icons.</summary>
internal sealed class WeatherConditionsPresentation : IDisposable
{
    private const float Diameter = 24, Gap = 3;
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private HudOverlayPreparationContext? context;
    private WeatherConditionsSample? sample;
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
        int count = (sample.Precipitation.HasValue ? 1 : 0) + (sample.Fog ? 1 : 0);
        if (count > 0 && (icons == null || !ReferenceEquals(context?.Api, preparation.Api))) icons = load(preparation);
        if (count > 0 && (backdrop == null || !ReferenceEquals(context?.Api, preparation.Api))) backdrop = loadBackdrop(preparation);
        context = preparation;
        Size = count == 0 ? SizeF.Empty : new SizeF(sample.Fog ? 2 * Diameter + Gap : Diameter, Diameter);
    }
    /// <summary>Draws only borrowed prepared textures; the host owns clipping and GUI visibility.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (sample == null || icons == null || context == null) return;
        double pixels = Diameter * context.GuiScale;
        double x = bounds.renderX, y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        if (sample.Precipitation is EnumPrecipitationType type)
        {
            backdrop?.Draw(renderer, x, y, context.GuiScale);
            int index = type switch { EnumPrecipitationType.Snow => 1, EnumPrecipitationType.Hail => 2, _ => 0 };
            renderer.RenderTexture(icons[index].TextureId, x, y, pixels, pixels, 50);
        }
        // Fog owns the second slot even when precipitation is absent.
        if (sample.Fog)
        {
            double fogX = x + (Diameter + Gap) * context.GuiScale;
            backdrop?.Draw(renderer, fogX, y, context.GuiScale);
            renderer.RenderTexture(icons[3].TextureId, fogX, y, pixels, pixels, 50);
        }
    }
    #endregion
    #region Lifetime
    /// <summary>Drops bindings without deleting resources owned by the engine cache.</summary>
    public void Reset()
    {
        icons = null;
        backdrop = null;
        context = null;
        sample = null;
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
        string[] names = ["rain", "snow", "hail", "fog"];
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/weather/" + names[index] + ".png"), ref texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion
}
