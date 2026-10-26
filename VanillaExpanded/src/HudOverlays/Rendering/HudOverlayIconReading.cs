using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using AssetLocation = Vintagestory.API.Common.AssetLocation;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Prepares a compact icon and optional normal-size reading without an instrument housing.</summary>
internal sealed class HudOverlayIconReading : IDisposable
{
    private readonly WorkshopGaugeText text;
    private readonly HudIndicatorStackLayout layout = new();
    private readonly Func<HudOverlayPreparationContext, string, LoadedTexture> load;
    private LoadedTexture? icon;
    private HudOverlayPreparationContext? context;
    private string? path;
    private bool ownsIcon, hasReading;

    #region Public API
    /// <summary>Accepts headless artwork and text factories while retaining explicit native resource ownership.</summary>
    public HudOverlayIconReading(WorkshopGaugeText? text = null,
        Func<HudOverlayPreparationContext, string, LoadedTexture>? load = null)
    {
        this.text = text ?? new();
        this.load = load ?? LoadIcon;
    }

    /// <summary>Gets the compact footprint in unscaled GUI units.</summary>
    public SizeF Size => layout.Size;

    /// <summary>Loads only changed artwork and prepares the optional reading outside drawing.</summary>
    public void Prepare(HudOverlayPreparationContext preparation, string assetPath, string? reading = null)
    {
        if (icon == null || path != assetPath || !ReferenceEquals(context?.Api, preparation.Api))
        {
            // SVG loads create a new texture; PNG loads borrow the engine cache. Never delete a borrowed texture.
            var replacement = load(preparation, assetPath);
            if (ownsIcon) icon?.Dispose();
            icon = replacement;
            ownsIcon = assetPath.EndsWith(".svg", StringComparison.Ordinal);
            path = assetPath;
        }
        hasReading = !string.IsNullOrEmpty(reading);
        if (hasReading) { text.SetContent(reading!); text.Prepare(preparation); }
        else text.Reset();
        float diameter = hasReading ? HudOverlaySizes.Small : HudOverlaySizes.Standard;
        layout.Configure(hasReading ? text.Size : SizeF.Empty, new SizeF(diameter, diameter), HudOverlaySizes.ReadingGap);
        context = preparation;
    }

    /// <summary>Centers the icon below its reading using only cached resources and shared opacity.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (icon == null || context == null) return;
        layout.Resolve(bounds, context.GuiScale);
        var artwork = layout.Icon;
        renderer.RenderTexture(icon.TextureId, artwork.renderX, artwork.renderY, artwork.OuterWidth, artwork.OuterHeight, 50, DrawOpacity.Tint);
        if (hasReading)
        {
            var reading = layout.Reading;
            text.Draw(renderer, new RectangleF((float)reading.renderX, (float)reading.renderY,
                (float)reading.OuterWidth, (float)reading.OuterHeight), 51);
        }
    }

    /// <summary>Releases owned text and SVG resources while dropping borrowed PNG bindings.</summary>
    public void Reset()
    {
        text.Reset();
        layout.Configure(SizeF.Empty, new SizeF(HudOverlaySizes.Standard, HudOverlaySizes.Standard), HudOverlaySizes.ReadingGap);
        if (ownsIcon) icon?.Dispose();
        icon = null; context = null; path = null; ownsIcon = false; hasReading = false;
    }

    /// <summary>Ends all compact presentation ownership.</summary>
    public void Dispose()
    {
        Reset();
        text.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Rasterizes SVG sources once or borrows cached PNG artwork, with common trilinear sampling.</summary>
    private static LoadedTexture LoadIcon(HudOverlayPreparationContext preparation, string path)
    {
        var location = new AssetLocation(Constants.ModId, path);
        LoadedTexture texture;
        if (path.EndsWith(".svg", StringComparison.Ordinal))
            texture = preparation.Api.Gui.LoadSvg(location, 64, 64, 64, 64);
        else
        {
            texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(location, ref texture);
            texture.IgnoreUndisposed = true;
        }
        return HudOverlayTextureFiltering.Prepare(texture);
    }
    #endregion
}
