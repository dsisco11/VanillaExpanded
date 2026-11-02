using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Measures normal HUD lettering with the same boxing and padding as native text textures.</summary>
internal static class TextMetrics
{
    #region Public API
    public const double MinimumOutlineWidth = 1.5;

    /// <summary>Creates the shared contrast font without allocating a GPU resource.</summary>
    public static CairoFont Font(double strokeWidth = MinimumOutlineWidth)
    {
        if (!double.IsFinite(strokeWidth) || strokeWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        return CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text)
            .WithStroke(new double[] { 0, 0, 0, .85 }, strokeWidth == 0 ? 0 : Math.Max(MinimumOutlineWidth, strokeWidth));
    }

    /// <summary>Predicts native texture dimensions during canonical controller preparation.</summary>
    public static SizeF Measure(HudOverlayPreparationContext context, string value, double strokeWidth = MinimumOutlineWidth)
    {
        if (string.IsNullOrEmpty(value)) return SizeF.Empty;
        var font = Font(strokeWidth);
        var bounds = new ElementBounds();
        font.AutoBoxSize(value, bounds);
        // TextTextureUtil adds one pixel of boxing and twice the rounded stroke padding before scaling.
        double padding = 1 + 2 * Math.Ceiling(font.StrokeWidth);
        return new((float)(Math.Ceiling((bounds.fixedWidth + padding) * context.GuiScale) / context.GuiScale),
            (float)(Math.Ceiling((bounds.fixedHeight + padding) * context.GuiScale) / context.GuiScale));
    }
    #endregion
}
