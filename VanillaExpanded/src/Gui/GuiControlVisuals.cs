using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal static class GuiControlPalette
{
    internal const int DialogTop = 0x4b4030;
    internal const int DialogBottom = 0x3b3226;
    internal const int DialogOutline = 0x817159;
    internal const int TitleTop = 0x65553e;
    internal const int TitleBottom = 0x4a3c2c;
    internal const int TitleSeparator = 0x2a221a;
    internal const int Text = 0xe8ddc5;
    internal const int PanelFill = 0x51432f;
    internal const int PanelOutline = 0x887453;
    internal const int TabFill = 0x382f23;
    internal const int TabOutline = 0x766349;
    internal const int HoverFill = 0x51422e;
    internal const int Icon = 0xb7a98c;
    internal const int HoverIcon = 0xfff0cc;
    internal const int SelectedIcon = 0xf5d58d;
    internal const int SelectedOutline = 0xaa8c54;
    internal const int SelectedAccent = 0xd3b46f;
    internal const int Heading = 0xc8b897;
    internal const int CheckboxFill = 0x30291e;
    internal const int CheckboxOutline = 0x948366;
    internal const int DropdownFill = 0x382f23;
    internal const int DropdownOutline = 0x74634a;
    internal const int DropdownText = 0xd7c8aa;
    internal const int SearchFill = 0x30281e;
    internal const int SearchOutline = 0x8c795b;
    internal const int Placeholder = 0xa99b80;
    internal const int ListFill = 0x352d22;
    internal const int ListOutline = 0x2c251c;
    internal const int ScrollbarTrack = 0x30291e;
    internal const int ScrollbarThumb = 0x8d7957;
    internal const int ScrollbarHover = 0xc8b897;
    internal const int ScrollbarPressed = 0xf5d58d;
    internal const int ButtonText = 0xfff0cc;
    internal const int ButtonFill = 0x80613d;
    internal const int ButtonOutline = 0xc29a5b;
    internal const int ButtonHoverFill = 0x8b693f;
    internal const int ButtonEmphasisOutline = 0xe0bc79;
    internal const int ButtonPressedFill = 0x604627;
    internal const int ButtonDisabledFill = 0x3b342b;
    internal const int ButtonDisabledOutline = 0x625747;
    internal const double DisabledOpacity = 0.35;

    internal static double[] Color(int rgb, double opacity = 1)
    {
        if (!double.IsFinite(opacity) || opacity < 0 || opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        return [((rgb >> 16) & 255) / 255d, ((rgb >> 8) & 255) / 255d, (rgb & 255) / 255d, opacity];
    }
}

internal static class GuiControlMetrics
{
    internal const double Outline = 1;
    internal const double TabWidth = 30;
    internal const double TabHeight = 36;
    internal const double InactiveTabHeight = 32;
    internal const double TabGap = 4;
    internal const double CategoryIconSize = 20;
    internal const double TabAccent = 2;
    internal const double TabCornerRadius = 5;
    internal const double CategoryIconBottomInset = 7;
    internal const double TextInset = 8;
    internal const double CheckboxSize = 17;
    internal const double SearchHeight = 28;
    internal const float TextFontSize = 14;
    internal const float CompactFontSize = 12;
    internal const double ClearSize = 16;
    internal const double ClearInset = 5;
    internal const double NativeTextInsetPixels = 3;
    internal const double NativeDropdownHeight = 30;
    internal const double NativePopupRowHeight = 30;
    internal const double NativeArrowWidth = 20;
    internal const double ChevronSize = 8;
    internal const double DropdownTextInset = 5;
    internal const double PopupSelectionWidth = 1;
    internal const double ClearStroke = 1.5;
    internal const double ScrollbarTrackWidth = 10;
    internal const double ScrollbarThumbWidth = 6;
    internal const int ScrollbarMinimumThumbPixels = 10;

    internal static double MeasureTextWidth(CairoFont font, string text, double inset = TextInset) =>
        Math.Ceiling(font.GetTextExtents(text).Width / GuiElement.scaled(1) + 2 * inset);

    internal static (ElementBounds Editable, ElementBounds Clear) InputBounds(ElementBounds field)
    {
        if (!double.IsFinite(field.fixedHeight) || field.fixedHeight < ClearSize)
            throw new ArgumentException("Text field is too short for its clear control.", nameof(field));
        double left = TextInset - NativeTextInsetPixels / GuiElement.scaled(1);
        double width = field.fixedWidth - left - ClearSize - 2 * ClearInset;
        if (!double.IsFinite(width) || width <= 0)
            throw new ArgumentException("Text field is too narrow for its editable and clear areas.", nameof(field));
        var editable = field.FlatCopy().WithFixedPosition(field.fixedX + left, field.fixedY).WithFixedWidth(width);
        var clear = ElementBounds.Fixed(field.fixedX + field.fixedWidth - ClearSize - ClearInset,
            field.fixedY + (field.fixedHeight - ClearSize) / 2, ClearSize, ClearSize);
        return (editable, clear);
    }
}

internal static class GuiControlDrawing
{
    internal static void FrameTexture(ICoreClientAPI api, int width, int height, int fill, int outline,
        double scale, ref LoadedTexture texture)
    {
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(surface);
        Frame(ctx, 0, 0, width, height, fill, outline, scale);
        api.Gui.LoadOrUpdateCairoTexture(surface, false, ref texture);
    }

    internal static int Pixels(double units, double scale)
    {
        if (!double.IsFinite(units) || units < 0) throw new ArgumentOutOfRangeException(nameof(units));
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        return checked((int)Math.Round(units * scale, MidpointRounding.AwayFromZero));
    }

    internal static void Frame(Context ctx, double x, double y, double width, double height,
        int fill, int outline, double scale)
    {
        ValidateRectangle(x, y, width, height);
        ctx.Save();
        try
        {
            int stroke = Math.Max(1, Pixels(GuiControlMetrics.Outline, scale));
            x = Math.Round(x, MidpointRounding.AwayFromZero);
            y = Math.Round(y, MidpointRounding.AwayFromZero);
            width = Math.Round(width, MidpointRounding.AwayFromZero);
            height = Math.Round(height, MidpointRounding.AwayFromZero);
            if (width < stroke || height < stroke) throw new ArgumentOutOfRangeException(nameof(width), "Frame is smaller than its outline.");
            ctx.SetSourceRGBA(GuiControlPalette.Color(fill));
            ctx.Rectangle(x, y, width, height);
            ctx.Fill();
            ctx.SetSourceRGBA(GuiControlPalette.Color(outline));
            ctx.LineWidth = stroke;
            ctx.Rectangle(x + stroke / 2d, y + stroke / 2d, width - stroke, height - stroke);
            ctx.Stroke();
        }
        finally { ctx.Restore(); }
    }

    internal static void Gradient(Context ctx, double x, double y, double width, double height, int top, int bottom)
    {
        ValidateRectangle(x, y, width, height);
        ctx.Save();
        try
        {
            using var gradient = new LinearGradient(x, y, x, y + height);
            gradient.AddColorStop(0, new Color(((top >> 16) & 255) / 255d, ((top >> 8) & 255) / 255d, (top & 255) / 255d));
            gradient.AddColorStop(1, new Color(((bottom >> 16) & 255) / 255d, ((bottom >> 8) & 255) / 255d, (bottom & 255) / 255d));
            ctx.SetSource(gradient);
            ctx.Rectangle(x, y, width, height);
            ctx.Fill();
        }
        finally { ctx.Restore(); }
    }

    private static void ValidateRectangle(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Frame coordinates must be finite.");
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    }

    internal static (double X, double Y) RenderOrigin(ElementBounds bounds, int rasterHeight)
    {
        var surface = DrawingSurface(bounds);
        if (surface == null)
            return (Math.Round(bounds.renderX, MidpointRounding.AwayFromZero),
                Math.Round(bounds.renderY, MidpointRounding.AwayFromZero));

        // Native static textures truncate their host origin; anchor the bottom to that same surface.
        return ((int)surface.renderX + Math.Round(bounds.renderX - surface.renderX, MidpointRounding.AwayFromZero),
            (int)surface.renderY + Math.Round(bounds.renderY - surface.renderY + bounds.OuterHeight,
                MidpointRounding.AwayFromZero) - rasterHeight);
    }

    internal static int RenderWidth(ElementBounds bounds)
    {
        if (!double.IsFinite(bounds.OuterWidth) || bounds.OuterWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), "Raster width must be positive and finite.");
        double x = bounds.renderX - (DrawingSurface(bounds)?.renderX ?? 0);
        return Math.Max(1, checked((int)(Math.Round(x + bounds.OuterWidth, MidpointRounding.AwayFromZero)
            - Math.Round(x, MidpointRounding.AwayFromZero))));
    }

    private static ElementBounds? DrawingSurface(ElementBounds bounds)
    {
        ElementBounds? surface = null;
        for (var parent = bounds.ParentBounds; parent != null; parent = parent.ParentBounds)
            if (parent.IsDrawingSurface) surface = parent;
        return surface;
    }
}
