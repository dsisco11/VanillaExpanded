using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

/// <summary>Draws outlined glyphs without filling glyph paths as foreground lettering.</summary>
internal static class TextDrawing
{
    public static void DrawTextLine(Context context, CairoFont font, string text, double x = 0, double y = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        ValidateFont(font);
        if (text.Length == 0) return;

        using var path = context.CopyPath();
        context.Save();
        try
        {
            font.SetupContext(context);
            double baseline = (int)(y + context.FontExtents.Ascent);
            x = (int)x;
            context.NewPath();
            if (font.StrokeWidth > 0)
            {
                context.Save();
                try
                {
                    context.MoveTo(x, baseline);
                    context.TextPath(text);
                    context.LineWidth = font.StrokeWidth;
                    context.SetSourceRGBA(font.StrokeColor);
                    context.Stroke();
                }
                finally { context.Restore(); }
            }
            context.MoveTo(x, baseline);
            context.ShowText(text);
            if (font.RenderTwice)
            {
                context.MoveTo(x, baseline);
                context.ShowText(text);
            }
            if (context.Status != Status.Success)
                throw new InvalidOperationException("Cairo text drawing failed: " + context.Status);
        }
        finally
        {
            context.Restore();
            context.NewPath();
            context.AppendPath(path);
        }
    }

    /// <summary>Retains native line breaking and alignment, replacing only the painting operation.</summary>
    public static void DrawMultilineText(Context context, CairoFont font, string text, double width,
        EnumTextOrientation orientation = EnumTextOrientation.Left, double x = 0, double y = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!Enum.IsDefined(orientation)) throw new ArgumentOutOfRangeException(nameof(orientation));
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        ValidateFont(font);
        var lines = new TextDrawUtil().Lineize(font, text, width, EnumLinebreakBehavior.AfterWord);
        foreach (var line in lines)
        {
            double offset = orientation switch
            {
                EnumTextOrientation.Center => (line.LeftSpace + line.RightSpace) / 2,
                EnumTextOrientation.Right => line.LeftSpace + line.RightSpace,
                _ => 0
            };
            DrawTextLine(context, font, line.Text, (int)x + line.Bounds.X + offset, (int)y + line.Bounds.Y);
        }
    }

    /// <summary>Uses the shared native-compatible boxing, including rounded outline padding.</summary>
    public static LoadedTexture CreateTexture(ICoreClientAPI api, string text, CairoFont font)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        ValidateFont(font);
        var pixels = CairoTextSizing.PixelSize(text, font);
        int padding = (int)Math.Ceiling(font.StrokeWidth);
        return CreateTexture(api, text, font, pixels.Width, pixels.Height,
            paddingX: padding, paddingY: padding);
    }

    /// <summary>Uploads premultiplied Cairo pixels once; callers retain texture ownership and sampling policy.</summary>
    public static LoadedTexture CreateTexture(ICoreClientAPI api, string text, CairoFont font,
        int width, int height, EnumTextOrientation orientation = EnumTextOrientation.Left,
        int paddingX = 0, int paddingY = 0)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (paddingX < 0) throw new ArgumentOutOfRangeException(nameof(paddingX));
        if (paddingY < 0) throw new ArgumentOutOfRangeException(nameof(paddingY));
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var context = new Context(surface);
        DrawMultilineText(context, font, text, width, orientation, paddingX, paddingY);
        var texture = new LoadedTexture(api);
        try
        {
            api.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static void ValidateFont(CairoFont font)
    {
        if (!double.IsFinite(font.UnscaledFontsize) || font.UnscaledFontsize <= 0
            || !double.IsFinite(font.StrokeWidth) || font.StrokeWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(font));
        if (font.StrokeWidth > 0 && font.StrokeColor == null)
            throw new ArgumentException("An outlined font must supply an outline color.", nameof(font));
    }
}
