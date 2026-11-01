using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementStyledCheckbox : GuiElementSwitch
{
    private LoadedTexture idle;
    private LoadedTexture hover;
    private LoadedTexture check;
    private LoadedTexture disabledCheck;
    private bool disposed;

    internal GuiElementStyledCheckbox(ICoreClientAPI api, Action<bool> changed, ElementBounds bounds)
        : base(api, changed, bounds, GuiControlMetrics.CheckboxSize, 0)
    {
        idle = new(api);
        hover = new(api);
        check = new(api);
        disabledCheck = new(api);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        int size = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.CheckboxSize, scaled(1)));
        GuiControlDrawing.FrameTexture(api, size, size, GuiControlPalette.CheckboxFill,
            GuiControlPalette.CheckboxOutline, scaled(1), ref idle);
        GuiControlDrawing.FrameTexture(api, size, size, GuiControlPalette.CheckboxFill,
            GuiControlPalette.SelectedOutline, scaled(1), ref hover);
        ComposeCheck(size, 1, ref check);
        ComposeCheck(size, GuiControlPalette.DisabledOpacity, ref disabledCheck);
    }

    private void ComposeCheck(int size, double opacity, ref LoadedTexture texture)
    {
        using var surface = new ImageSurface(Format.Argb32, size, size);
        using var ctx = new Context(surface);
        ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.SelectedIcon, opacity));
        ctx.LineWidth = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.Outline, scaled(1)));
        double inset = scaled(GuiControlMetrics.ClearInset);
        ctx.MoveTo(inset, size / 2d);
        ctx.LineTo(size / 2d, size - inset);
        ctx.LineTo(size - inset, inset);
        ctx.Stroke();
        generateTexture(surface, ref texture, linearMag: false);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        var background = Enabled && Bounds.PointInside(api.Input.MouseX, api.Input.MouseY) ? hover : idle;
        api.Render.Render2DTexturePremultipliedAlpha(background.TextureId, Bounds);
        if (On)
            api.Render.Render2DTexturePremultipliedAlpha((Enabled ? check : disabledCheck).TextureId, Bounds);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (Enabled) base.OnKeyDown(api, args);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        idle.Dispose();
        hover.Dispose();
        check.Dispose();
        disabledCheck.Dispose();
        base.Dispose();
    }
}
