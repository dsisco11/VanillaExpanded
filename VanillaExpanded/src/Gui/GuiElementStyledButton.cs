using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementStyledButton : GuiElementTextButton
{
    private readonly CairoFont normalFont;
    private readonly CairoFont pressedFont;
    private LoadedTexture idle;
    private LoadedTexture hover;
    private LoadedTexture pressed;
    private LoadedTexture disabled;
    private bool mousePressed;
    private bool active;
    private bool composed;
    private bool disposed;

    internal GuiElementStyledButton(ICoreClientAPI api, string text, ActionConsumable clicked,
        ElementBounds bounds, CairoFont? font = null)
        : this(api, text, clicked, bounds, MakeFonts(font)) { }

    private GuiElementStyledButton(ICoreClientAPI api, string text, ActionConsumable clicked,
        ElementBounds bounds, (CairoFont Normal, CairoFont Pressed) fonts)
        : base(api, text, fonts.Normal, fonts.Pressed, clicked,
            bounds.WithFixedPadding(GuiControlMetrics.TextInset, bounds.fixedPaddingY), EnumButtonStyle.None)
    {
        normalFont = fonts.Normal;
        pressedFont = fonts.Pressed;
        idle = new(api);
        hover = new(api);
        pressed = new(api);
        disabled = new(api);
    }

    private static (CairoFont, CairoFont) MakeFonts(CairoFont? font)
    {
        var normal = (font ?? CairoFont.WhiteSmallText().WithFontSize(GuiControlMetrics.CompactFontSize))
            .Clone().WithColor(GuiControlPalette.Color(GuiControlPalette.ButtonText));
        return (normal, normal.Clone());
    }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            if (base.Enabled == value) return;
            base.Enabled = value;
            if (composed) ComposeNativeText();
        }
    }

    public new void SetActive(bool value)
    {
        base.SetActive(value);
        active = value;
    }

    private void ComposeNativeText()
    {
        normalFont.Color[3] = pressedFont.Color[3] = Enabled ? 1 : GuiControlPalette.DisabledOpacity;
        using var surface = new ImageSurface(Format.Argb32, 1, 1);
        using var ctx = new Context(surface);
        base.ComposeElements(ctx, surface);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        ComposeNativeText();
        composed = true;
        int width = Math.Max(1, (int)Math.Round(Bounds.OuterWidth, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(Bounds.OuterHeight, MidpointRounding.AwayFromZero));
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.ButtonFill,
            GuiControlPalette.ButtonOutline, scaled(1), ref idle);
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.ButtonHoverFill,
            GuiControlPalette.ButtonEmphasisOutline, scaled(1), ref hover);
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.ButtonPressedFill,
            GuiControlPalette.ButtonEmphasisOutline, scaled(1), ref pressed);
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.ButtonDisabledFill,
            GuiControlPalette.ButtonDisabledOutline, scaled(1), ref disabled);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!Visible) return;
        var texture = !Enabled ? disabled : active || mousePressed ? pressed
            : Bounds.PointInside(api.Input.MouseX, api.Input.MouseY) ? hover : idle;
        api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, Bounds);
        base.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        if (Visible && Enabled) mousePressed = true;
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUpOnElement(api, args);
        mousePressed = false;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        if (Visible) mousePressed = false;
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
