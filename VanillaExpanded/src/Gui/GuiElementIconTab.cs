using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementIconTab : GuiElementToggleButton
{
    private readonly string iconName;
    private LoadedTexture releasedBackground;
    private LoadedTexture hoveredBackground;
    private LoadedTexture selectedBackground;
    private LoadedTexture releasedIcon;
    private LoadedTexture hoveredIcon;
    private LoadedTexture selectedIcon;
    private int iconSize;
    private bool disposed;

    internal GuiElementIconTab(ICoreClientAPI api, string iconName, CairoFont font,
        Action<bool> changed, ElementBounds bounds)
        : base(api, "", "", font, changed, bounds, true)
    {
        this.iconName = iconName;
        releasedBackground = new LoadedTexture(api);
        hoveredBackground = new LoadedTexture(api);
        selectedBackground = new LoadedTexture(api);
        releasedIcon = new LoadedTexture(api);
        hoveredIcon = new LoadedTexture(api);
        selectedIcon = new LoadedTexture(api);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        ComposeBackgrounds(GuiControlDrawing.RenderWidth(Bounds));
        iconSize = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.CategoryIconSize, scaled(1)));
        ComposeIcon(iconSize, GuiControlPalette.Color(GuiControlPalette.Icon), ref releasedIcon);
        ComposeIcon(iconSize, GuiControlPalette.Color(GuiControlPalette.HoverIcon), ref hoveredIcon);
        ComposeIcon(iconSize, GuiControlPalette.Color(GuiControlPalette.SelectedIcon), ref selectedIcon);
    }

    internal static void DrawPanel(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        GuiControlDrawing.Frame(ctx, bounds.drawX, bounds.drawY, bounds.OuterWidth, bounds.OuterHeight,
            GuiControlPalette.PanelFill, GuiControlPalette.PanelOutline, scaled(1));
    }

    private void ComposeBackgrounds(int width)
    {
        ComposeBackground(false, GuiControlPalette.TabFill, width, ref releasedBackground);
        ComposeBackground(false, GuiControlPalette.HoverFill, width, ref hoveredBackground);
        ComposeBackground(true, GuiControlPalette.PanelFill, width, ref selectedBackground);
    }

    private void ComposeBackground(bool selected, int fill, int width, ref LoadedTexture texture)
    {
        int height = Math.Max(1, (int)Math.Round(Bounds.OuterHeight, MidpointRounding.AwayFromZero));
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = genContext(surface);
        int stroke = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.Outline, scaled(1)));
        double top = selected ? 0 : height - GuiControlDrawing.Pixels(GuiControlMetrics.InactiveTabHeight, scaled(1));
        double radius = Math.Min(GuiControlDrawing.Pixels(GuiControlMetrics.TabCornerRadius, scaled(1)),
            (width - stroke) / 2d);
        TabPath(ctx, width, height, top, radius, stroke);
        ctx.SetSourceRGBA(GuiControlPalette.Color(fill));
        ctx.FillPreserve();
        if (selected)
        {
            ctx.Save();
            ctx.Clip();
            ctx.Rectangle(0, stroke, width, GuiControlDrawing.Pixels(GuiControlMetrics.TabAccent, scaled(1)));
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.SelectedAccent));
            ctx.Fill();
            ctx.Restore();
            TabPath(ctx, width, height, top, radius, stroke);
        }
        ctx.SetSourceRGBA(GuiControlPalette.Color(selected ? GuiControlPalette.SelectedOutline : GuiControlPalette.TabOutline));
        ctx.LineWidth = stroke;
        ctx.Stroke();
        if (!selected)
        {
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.PanelOutline));
            ctx.Rectangle(0, height - stroke, width, stroke);
            ctx.Fill();
        }
        generateTexture(surface, ref texture, linearMag: false);
    }

    private static void TabPath(Context ctx, int width, int height, double top, double radius, int stroke)
    {
        double inset = stroke / 2d;
        ctx.MoveTo(inset, height);
        ctx.LineTo(inset, top + radius + inset);
        ctx.Arc(radius + inset, top + radius + inset, radius, Math.PI, 1.5 * Math.PI);
        ctx.LineTo(width - radius - inset, top + inset);
        ctx.Arc(width - radius - inset, top + radius + inset, radius, 1.5 * Math.PI, 2 * Math.PI);
        ctx.LineTo(width - inset, height);
    }

    private void ComposeIcon(int size, double[] color, ref LoadedTexture texture)
    {
        using var surface = new ImageSurface(Format.Argb32, size, size);
        using var ctx = genContext(surface);
        api.Gui.Icons.DrawIcon(ctx, iconName, 0, 0, size, size, color);
        generateTexture(surface, ref texture, linearMag: false);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        int width = GuiControlDrawing.RenderWidth(Bounds);
        if (releasedBackground.Width != width) ComposeBackgrounds(width);
        var (x, y) = GuiControlDrawing.RenderOrigin(Bounds, releasedBackground.Height);
        bool hovered = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        var background = On ? selectedBackground : hovered ? hoveredBackground : releasedBackground;
        api.Render.Render2DTexturePremultipliedAlpha(
            background.TextureId, x, y, background.Width, background.Height, 50);
        api.Render.Render2DTexturePremultipliedAlpha(
            (On ? selectedIcon : hovered ? hoveredIcon : releasedIcon).TextureId,
            x + Math.Round((background.Width - iconSize) / 2d, MidpointRounding.AwayFromZero),
            y + background.Height - GuiControlDrawing.Pixels(GuiControlMetrics.CategoryIconBottomInset, scaled(1)) - iconSize,
            iconSize, iconSize, 51);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        releasedBackground.Dispose();
        hoveredBackground.Dispose();
        selectedBackground.Dispose();
        releasedIcon.Dispose();
        hoveredIcon.Dispose();
        selectedIcon.Dispose();
        base.Dispose();
    }
}
