using System;
using Cairo;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Prepares runtime solar tints and dividers in the registered circular face aperture.</summary>
internal static class ApproximateTimeFaceArtwork
{
    #region Public API
    /// <summary>Prepares the colored face and dividers as owned pixels outside the draw path.</summary>
    public static LoadedTexture Create(HudOverlayPreparationContext preparation, ApproximateTimeDayPhases phases)
    {
        using var surface = new ImageSurface(Format.Argb32, RadialGaugeLayout.Canvas, RadialGaugeLayout.Canvas);
        using var drawing = new Context(surface);
        // Keep a fine separation between hourly colors without exposing broad dark spokes.
        const double segmentInset = .002;
        for (int hour = 0; hour < 24; hour++)
        {
            double start = (hour - 12.5) * Math.PI / 12 - Math.PI / 2;
            drawing.MoveTo(RadialGaugeLayout.CenterX, RadialGaugeLayout.CenterY);
            drawing.Arc(RadialGaugeLayout.CenterX, RadialGaugeLayout.CenterY, RadialGaugeLayout.ApertureRadius - 2,
                start + segmentInset, start + Math.PI / 12 - segmentInset);
            drawing.ClosePath();
            if (ReferenceEquals(phases, ApproximateTimeDayPhases.Neutral)) drawing.SetSourceRGB(.18, .18, .17);
            else drawing.SetSourceRGB(phases.Channel(hour, 0), phases.Channel(hour, 1), phases.Channel(hour, 2));
            drawing.Fill();
        }
        var texture = new LoadedTexture(preparation.Api);
        try
        {
            preparation.Api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
            return HudOverlayTextureFiltering.Prepare(texture);
        }
        catch { texture.Dispose(); throw; }
    }
    #endregion
}

