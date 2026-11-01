using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementIconToggleButton : GuiElementToggleButton
{
    private readonly string iconName;
    private LoadedTexture releasedIcon;
    private LoadedTexture activeIcon;
    private int iconSize;
    private bool disposed;

    internal GuiElementIconToggleButton(ICoreClientAPI api, string iconName, CairoFont font,
        Action<bool> changed, ElementBounds bounds, bool toggleable = false)
        : base(api, "", "", font, changed, bounds, toggleable)
    {
        this.iconName = iconName;
        releasedIcon = new LoadedTexture(api);
        activeIcon = new LoadedTexture(api);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        base.ComposeElements(ctx, surface);
        iconSize = Math.Max(1, (int)Math.Round(
            Math.Min(Bounds.InnerWidth, Bounds.InnerHeight) - scaled(8), MidpointRounding.AwayFromZero));
        ComposeIcon(iconSize, Font.Color, ref releasedIcon);
        ComposeIcon(iconSize, GuiStyle.ActiveButtonTextColor, ref activeIcon);
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
        base.RenderInteractiveElements(deltaTime);
        bool active = On || Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        api.Render.Render2DTexturePremultipliedAlpha(
            active ? activeIcon.TextureId : releasedIcon.TextureId,
            Math.Round(Bounds.renderX + Bounds.absPaddingX + scaled(4), MidpointRounding.AwayFromZero),
            Math.Round(Bounds.renderY + Bounds.absPaddingY + scaled(4), MidpointRounding.AwayFromZero),
            iconSize, iconSize, 51);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        releasedIcon.Dispose();
        activeIcon.Dispose();
        base.Dispose();
    }
}
