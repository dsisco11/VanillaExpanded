using System;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Gui;

internal static class CairoTextSizing
{
    public static Size PixelSize(string text, CairoFont font)
    {
        var bounds = new ElementBounds();
        font.AutoBoxSize(text, bounds);
        double padding = 1 + 2 * Math.Ceiling(font.StrokeWidth);
        return new((int)Math.Ceiling((bounds.fixedWidth + padding) * RuntimeEnv.GUIScale),
            (int)Math.Ceiling((bounds.fixedHeight + padding) * RuntimeEnv.GUIScale));
    }

    public static CairoFont FitRectangle(CairoFont font, string text, SizeF available, double minimumStrokeWidth = 0) => Fit(font, candidate =>
    {
        var pixels = PixelSize(text, candidate);
        return pixels.Width <= available.Width && pixels.Height <= available.Height;
    }, minimumStrokeWidth);

    // Fit the font, including its outline, before Cairo creates any pixels. Impossibly small
    // apertures clip the minimum-size raster instead of resampling it.
    public static CairoFont Fit(CairoFont font, Func<CairoFont, bool> fits, double minimumStrokeWidth = 0)
    {
        if (!double.IsFinite(minimumStrokeWidth) || minimumStrokeWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumStrokeWidth));
        if (font.StrokeWidth > 0) font.StrokeWidth = Math.Max(minimumStrokeWidth, font.StrokeWidth);
        if (fits(font)) return font;
        double preferred = font.UnscaledFontsize, stroke = font.StrokeWidth;
        // Below one physical pixel, Cairo's Windows backend can select a default-size font.
        double lower = Math.Min(1 / RuntimeEnv.GUIScale, preferred), upper = preferred;
        void Apply(double size)
        {
            font.WithFontSize((float)size);
            font.StrokeWidth = stroke * size / preferred;
            if (stroke > 0) font.StrokeWidth = Math.Max(minimumStrokeWidth, font.StrokeWidth);
        }
        Apply(lower);
        if (!fits(font)) return font;
        for (int iteration = 0; iteration < 16; iteration++)
        {
            double middle = (lower + upper) / 2;
            Apply(middle);
            if (fits(font)) lower = middle;
            else upper = middle;
        }
        Apply(lower);
        return font;
    }
}
