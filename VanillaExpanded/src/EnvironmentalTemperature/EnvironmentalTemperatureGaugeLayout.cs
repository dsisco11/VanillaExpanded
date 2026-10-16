using System;
using System.Drawing;
using Cairo;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Defines one calibration for liquid reveal, runtime ticks, and engraved scale labels.</summary>
internal static class EnvironmentalTemperatureGaugeLayout
{
    public static readonly SizeF Canvas = new(512, 128);
    public static readonly RectangleF Reading = new(419, 38, 59, 31);
    public const float TubeStart = 96, TubeEnd = 395;

    #region Public API
    /// <summary>Clamps only the visual fill while preserving the original numeric reading elsewhere.</summary>
    public static float Fill(float celsius) => Math.Clamp((celsius + 40) / 100, 0, 1);

    /// <summary>Converts a calibrated degree value to its shared authored horizontal position.</summary>
    public static float Position(float celsius) => TubeStart + (TubeEnd - TubeStart) * Fill(celsius);

    /// <summary>Maps the full bulb and progressively revealed straight tube through the current instrument size.</summary>
    public static RectangleF LiquidClip(RectangleF screen, float celsius) => WorkshopGaugeArtwork.Map(screen, Canvas,
        new RectangleF(0, 0, Position(celsius), Canvas.Height));

    /// <summary>Prepares embossed five-degree ticks as owned pixels, leaving the source artwork unmarked.</summary>
    public static LoadedTexture CreateTicks(HudOverlayPreparationContext context)
    {
        using var surface = new ImageSurface(Format.Argb32, 512, 128);
        using var drawing = new Context(surface);
        for (int degree = -40; degree <= 60; degree += 5)
        {
            double x = Position(degree), length = degree % 20 == 0 ? 10 : degree % 10 == 0 ? 7 : 4;
            drawing.LineWidth = degree % 20 == 0 ? 2 : 1.3;
            drawing.SetSourceRGBA(.7, .52, .32, .8);
            drawing.MoveTo(x + 1, 70); drawing.LineTo(x + 1, 70 + length); drawing.Stroke();
            drawing.SetSourceRGBA(.13, .09, .055, 1);
            drawing.MoveTo(x, 69); drawing.LineTo(x, 69 + length); drawing.Stroke();
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
