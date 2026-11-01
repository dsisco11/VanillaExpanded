using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementIconTab : GuiElementToggleButton
{
    private static readonly double[] PanelColor = [81 / 255d, 67 / 255d, 47 / 255d];
    private static readonly double[] BorderColor = [136 / 255d, 116 / 255d, 83 / 255d];
    private readonly string iconName;
    private LoadedTexture releasedBackground;
    private LoadedTexture selectedBackground;
    private LoadedTexture releasedIcon;
    private LoadedTexture activeIcon;
    private int iconSize;
    private bool disposed;

    internal GuiElementIconTab(ICoreClientAPI api, string iconName, CairoFont font,
        Action<bool> changed, ElementBounds bounds)
        : base(api, "", "", font, changed, bounds, true)
    {
        this.iconName = iconName;
        releasedBackground = new LoadedTexture(api);
        selectedBackground = new LoadedTexture(api);
        releasedIcon = new LoadedTexture(api);
        activeIcon = new LoadedTexture(api);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        ComposeBackground(false, ref releasedBackground);
        ComposeBackground(true, ref selectedBackground);
        iconSize = Math.Max(1, (int)Math.Round(scaled(20), MidpointRounding.AwayFromZero));
        ComposeIcon(iconSize, Font.Color, ref releasedIcon);
        ComposeIcon(iconSize, GuiStyle.ActiveButtonTextColor, ref activeIcon);
    }

    internal static void DrawPanel(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        double x = Math.Round(bounds.drawX, MidpointRounding.AwayFromZero);
        double y = Math.Round(bounds.drawY, MidpointRounding.AwayFromZero);
        double width = Math.Round(bounds.OuterWidth, MidpointRounding.AwayFromZero);
        double height = Math.Round(bounds.OuterHeight, MidpointRounding.AwayFromZero);
        ctx.Save();
        ctx.SetSourceRGB(PanelColor[0], PanelColor[1], PanelColor[2]);
        ctx.Rectangle(x, y, width, height);
        ctx.Fill();
        ctx.SetSourceRGB(BorderColor[0], BorderColor[1], BorderColor[2]);
        ctx.LineWidth = 1;
        ctx.Rectangle(x + .5, y + .5, width - 1, height - 1);
        ctx.Stroke();
        ctx.Restore();
    }

    private void ComposeBackground(bool selected, ref LoadedTexture texture)
    {
        int width = Math.Max(1, (int)Math.Round(Bounds.OuterWidth, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(Bounds.OuterHeight, MidpointRounding.AwayFromZero));
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var ctx = genContext(surface);
        double top = selected ? 0 : Math.Round(scaled(4));
        double radius = Math.Min(scaled(5), (width - 1) / 2d);
        ctx.MoveTo(.5, height);
        ctx.LineTo(.5, top + radius + .5);
        ctx.Arc(radius + .5, top + radius + .5, radius, Math.PI, 1.5 * Math.PI);
        ctx.LineTo(width - radius - .5, top + .5);
        ctx.Arc(width - radius - .5, top + radius + .5, radius, 1.5 * Math.PI, 2 * Math.PI);
        ctx.LineTo(width - .5, height);
        var fill = selected ? PanelColor : GuiStyle.DialogStrongBgColor;
        ctx.SetSourceRGB(fill[0], fill[1], fill[2]);
        ctx.FillPreserve();
        var border = selected ? GuiStyle.ActiveButtonTextColor : BorderColor;
        ctx.SetSourceRGB(border[0], border[1], border[2]);
        ctx.LineWidth = 1;
        ctx.Stroke();
        if (!selected)
        {
            ctx.SetSourceRGB(BorderColor[0], BorderColor[1], BorderColor[2]);
            ctx.Rectangle(0, height - 1, width, 1);
            ctx.Fill();
        }
        generateTexture(surface, ref texture, linearMag: false);
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
        var (x, y) = GuiControlDrawing.RenderOrigin(Bounds, releasedBackground.Height);
        var background = On ? selectedBackground : releasedBackground;
        api.Render.Render2DTexturePremultipliedAlpha(
            background.TextureId, x, y, background.Width, background.Height, 50);
        bool active = On || Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        api.Render.Render2DTexturePremultipliedAlpha(
            active ? activeIcon.TextureId : releasedIcon.TextureId,
            x + Math.Round((background.Width - iconSize) / 2d, MidpointRounding.AwayFromZero),
            y + background.Height - Math.Round(scaled(7), MidpointRounding.AwayFromZero) - iconSize,
            iconSize, iconSize, 51);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        releasedBackground.Dispose();
        selectedBackground.Dispose();
        releasedIcon.Dispose();
        activeIcon.Dispose();
        base.Dispose();
    }
}
