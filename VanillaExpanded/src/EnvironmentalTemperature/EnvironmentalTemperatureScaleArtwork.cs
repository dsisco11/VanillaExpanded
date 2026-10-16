using System;
using Cairo;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Prepares runtime scale ticks inside the horseshoe tube without altering the instrument artwork.</summary>
internal static class EnvironmentalTemperatureScaleArtwork
{
    #region Public API
    /// <summary>Draws twenty-one correctly calibrated ticks with longer ten- and twenty-degree marks.</summary>
    public static LoadedTexture Create(HudOverlayPreparationContext context)
    {
        using var surface = new ImageSurface(Format.Argb32, 512, 512);
        using var drawing = new Context(surface);
        for (int degree = -40; degree <= 60; degree += 5)
        {
            float length = degree % 20 == 0 ? 14 : degree % 10 == 0 ? 10 : 6;
            var outer = EnvironmentalTemperatureGaugeLayout.Position(degree, 164);
            var inner = EnvironmentalTemperatureGaugeLayout.Position(degree, 164 - length);
            drawing.LineWidth = degree % 20 == 0 ? 2.5 : 1.7;
            drawing.SetSourceRGBA(.05, .035, .025, .9);
            drawing.MoveTo(outer.X + 1, outer.Y + 1); drawing.LineTo(inner.X + 1, inner.Y + 1); drawing.Stroke();
            drawing.SetSourceRGBA(.76, .67, .51, 1);
            drawing.MoveTo(outer.X, outer.Y); drawing.LineTo(inner.X, inner.Y); drawing.Stroke();
        }
        var texture = new LoadedTexture(context.Api);
        try
        {
            context.Api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
            return HudOverlayTextureFiltering.Prepare(texture);
        }
        catch { texture.Dispose(); throw; }
    }
    #endregion
}
