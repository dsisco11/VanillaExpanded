using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementStyledArrowButton : GuiElementToggleButton
{
    private readonly bool pointsUp;
    private LoadedTexture idle;
    private LoadedTexture hover;
    private LoadedTexture pressed;
    private LoadedTexture disabled;
    private bool disposed;

    internal GuiElementStyledArrowButton(ICoreClientAPI api, bool pointsUp, Action clicked, ElementBounds bounds)
        : base(api, string.Empty, string.Empty, CairoFont.WhiteSmallText(), _ => clicked(), bounds, toggleable: false)
    {
        this.pointsUp = pointsUp;
        idle = new(api);
        hover = new(api);
        pressed = new(api);
        disabled = new(api);
    }

    public override bool Focusable => false;
    internal float ZOffset { get; set; }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            if (!value) SetValue(false);
        }
    }

    private (int X, int Y, int Width, int Height) Measure()
    {
        int width = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.ScrollbarTrackWidth, scaled(1)));
        return (
            (int)Math.Round(Bounds.renderX + (Bounds.OuterWidth - width) / 2, MidpointRounding.AwayFromZero),
            (int)Math.Round(Bounds.renderY, MidpointRounding.AwayFromZero),
            width, Math.Max(0, (int)Math.Round(Bounds.OuterHeight, MidpointRounding.AwayFromZero)));
    }

    public override void ComposeElements(Context ctx, ImageSurface surface) => Recompose();

    internal void Recompose()
    {
        Bounds.CalcWorldBounds();
        var pixels = Measure();
        int height = Math.Max(1, pixels.Height);
        if (idle.Width == pixels.Width && idle.Height == height) return;
        ComposeArrow(pixels.Width, height, GuiControlPalette.ScrollbarThumb, 1, ref idle);
        ComposeArrow(pixels.Width, height, GuiControlPalette.ScrollbarHover, 1, ref hover);
        ComposeArrow(pixels.Width, height, GuiControlPalette.ScrollbarPressed, 1, ref pressed);
        ComposeArrow(pixels.Width, height, GuiControlPalette.ScrollbarThumb,
            GuiControlPalette.DisabledOpacity, ref disabled);
    }

    private void ComposeArrow(int width, int height, int color, double opacity, ref LoadedTexture texture)
    {
        using var image = new ImageSurface(Format.Argb32, width, height);
        using var ctx = new Context(image);
        int direction = pointsUp ? -1 : 1;
        ctx.SetSourceRGBA(GuiControlPalette.Color(color, opacity));
        ctx.MoveTo(width / 2d, Math.Round(height * (.5 + direction * .2), MidpointRounding.AwayFromZero));
        ctx.LineTo(Math.Round(width * .25, MidpointRounding.AwayFromZero),
            Math.Round(height * (.5 - direction * .2), MidpointRounding.AwayFromZero));
        ctx.LineTo(Math.Round(width * .75, MidpointRounding.AwayFromZero),
            Math.Round(height * (.5 - direction * .2), MidpointRounding.AwayFromZero));
        ctx.ClosePath();
        ctx.Fill();
        generateTexture(image, ref texture, linearMag: false);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        var pixels = Measure();
        bool inside = IsPositionInside(api.Input.MouseX, api.Input.MouseY);
        var texture = !Enabled ? disabled : inside ? On ? pressed : hover : idle;
        api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId,
            pixels.X, pixels.Y, pixels.Width, pixels.Height, 200 + ZOffset);
    }

    public override bool IsPositionInside(int posX, int posY) =>
        Bounds.OuterHeight > 0 && posY < Bounds.absY + Bounds.OuterHeight && base.IsPositionInside(posX, posY);

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!Enabled || !IsPositionInside(args.X, args.Y)) return;
        if (On) args.Handled = true;
        else base.OnMouseDownOnElement(api, args);
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        SetValue(false);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        idle.Dispose();
        hover.Dispose();
        pressed.Dispose();
        disabled.Dispose();
        base.Dispose();
    }
}
