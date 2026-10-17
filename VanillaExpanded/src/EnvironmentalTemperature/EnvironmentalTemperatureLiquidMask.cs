using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Owns a reusable texture containing an angular reveal of the authored full-liquid sprite.</summary>
internal sealed class EnvironmentalTemperatureLiquidMask : IDisposable
{
    private readonly System.Func<HudOverlayPreparationContext, int[]> loadSource;
    private readonly System.Func<LoadedTexture, LoadedTexture> filter;
    private ICoreClientAPI? api;
    private LoadedTexture? texture;
    private int[]? source, masked;
    private float preparedFill = float.NaN;

    #region Public API
    /// <summary>Accepts injectable CPU artwork and filtering boundaries without changing native texture ownership.</summary>
    public EnvironmentalTemperatureLiquidMask(System.Func<HudOverlayPreparationContext, int[]>? loadSource = null,
        System.Func<LoadedTexture, LoadedTexture>? filter = null)
    {
        this.loadSource = loadSource ?? LoadSource;
        this.filter = filter ?? HudOverlayTextureFiltering.Prepare;
    }

    /// <summary>Updates alpha coverage only when the bounded liquid level changes, reusing its GPU storage.</summary>
    public void Prepare(HudOverlayPreparationContext context, float fill)
    {
        if (source == null || !ReferenceEquals(api, context.Api))
        {
            Reset();
            int[] pixels = loadSource(context);
            if (pixels.Length != 512 * 512) throw new InvalidOperationException("The radial thermometer full layer must be 512 by 512 pixels.");
            source = pixels;
            masked = new int[pixels.Length];
            api = context.Api;
            texture = new LoadedTexture(api) { Width = 512, Height = 512 };
        }
        fill = Math.Clamp(fill, 0, 1);
        if (fill == preparedFill) return;
        for (int index = 0; index < source.Length; index++)
        {
            // Preserve authored RGB and glass reflections; only progress-mask alpha changes.
            uint pixel = (uint)source[index];
            int alpha = (int)(pixel >> 24);
            float coverage = alpha == 0 ? 0 : EnvironmentalTemperatureGaugeLayout.Coverage(index % 512 + .5f, index / 512 + .5f, fill);
            masked![index] = (int)((pixel & 0xffffff) | (uint)(alpha * coverage) << 24);
        }
        var target = texture!;
        context.Api.Render.LoadOrUpdateTextureFromBgra(masked!, true, 0, ref target);
        texture = filter(target);
        preparedFill = fill;
    }

    /// <summary>Draws prepared straight-alpha liquid pixels using the engine's standard GUI renderer.</summary>
    public void Draw(IRenderAPI renderer, RectangleF bounds)
    {
        if (texture == null) return;
        renderer.RenderTexture(texture.TextureId, bounds.X, bounds.Y, bounds.Width, bounds.Height, 51, HudOverlayDrawOpacity.Tint);
    }

    /// <summary>Releases owned GPU storage and drops CPU artwork at world exit or completed fade.</summary>
    public void Reset()
    {
        texture?.Dispose(); texture = null;
        source = null; masked = null; api = null; preparedFill = float.NaN;
    }

    /// <summary>Releases resource ownership at final removal.</summary>
    public void Dispose() => Reset();
    #endregion

    #region Private
    /// <summary>Decodes the full-state PNG once per client binding; runtime masking does not synthesize liquid shading.</summary>
    private static int[] LoadSource(HudOverlayPreparationContext context)
    {
        using var bitmap = context.Api.Assets.Get(new AssetLocation(Constants.ModId,
            "textures/hud/workshop-gauges/thermometer/full-glass.png")).ToBitmap(context.Api);
        if (bitmap.Width != 512 || bitmap.Height != 512) throw new InvalidOperationException("The radial thermometer full layer must be 512 by 512 pixels.");
        return bitmap.Pixels;
    }
    #endregion
}
