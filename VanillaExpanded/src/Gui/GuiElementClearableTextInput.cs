using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementClearableTextInput : GuiElementTextInput
{
    private readonly string placeholderText;
    private readonly CairoFont placeholderFont;
    private LoadedTexture idleFrame;
    private LoadedTexture hoverFrame;
    private LoadedTexture placeholder;
    private LoadedTexture disabledPlaceholder;
    private LoadedTexture clear;
    private LoadedTexture clearHover;
    private LoadedTexture clearDown;
    private bool clearPressed;
    private bool disposed;
    internal ElementBounds FieldBounds { get; }
    internal ElementBounds ClearBounds { get; }
    internal GuiElementHoverText ClearHint { get; }
    internal bool ClearVisible => Enabled && GetText().Length > 0;

    internal GuiElementClearableTextInput(ICoreClientAPI api, ElementBounds bounds,
        string placeholderText, string clearTooltip, Action<string> changed, CairoFont? font = null,
        CairoFont? placeholderFont = null)
        : base(api, GuiControlMetrics.InputBounds(bounds).Editable, changed,
            (font ?? CairoFont.WhiteSmallText().WithFontSize(GuiControlMetrics.TextFontSize)
                .WithColor(GuiControlPalette.Color(GuiControlPalette.Text))).Clone())
    {
        ArgumentNullException.ThrowIfNull(placeholderText);
        ArgumentNullException.ThrowIfNull(clearTooltip);
        ArgumentNullException.ThrowIfNull(changed);
        FieldBounds = bounds;
        ClearBounds = GuiControlMetrics.InputBounds(bounds).Clear;
        this.placeholderText = placeholderText;
        this.placeholderFont = placeholderFont?.Clone()
            ?? Font.Clone().WithColor(GuiControlPalette.Color(GuiControlPalette.Placeholder));
        idleFrame = new(api);
        hoverFrame = new(api);
        placeholder = new(api);
        disabledPlaceholder = new(api);
        clear = new(api);
        clearHover = new(api);
        clearDown = new(api);
        ClearHint = new(api, clearTooltip, Font.Clone(), 160, ClearBounds);
        ClearHint.SetAutoWidth(true);
        OnTextChanged = text =>
        {
            clearPressed = false;
            ClearHint.SetAutoDisplay(Enabled && text.Length > 0);
            if (text.Length == 0) ClearHint.SetVisible(false);
            changed(text);
        };
    }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            if (!value) clearPressed = false;
            base.Enabled = value;
            if (!value) ClearHint.SetVisible(false);
        }
    }

    public override void BeforeCalcBounds()
    {
        FieldBounds.ParentBounds = Bounds.ParentBounds;
        var geometry = GuiControlMetrics.InputBounds(FieldBounds);
        Bounds.fixedX = geometry.Editable.fixedX;
        Bounds.fixedY = geometry.Editable.fixedY;
        Bounds.fixedWidth = geometry.Editable.fixedWidth;
        Bounds.fixedHeight = geometry.Editable.fixedHeight;
        ClearBounds.ParentBounds = Bounds.ParentBounds;
        ClearBounds.WithFixedPosition(geometry.Clear.fixedX, geometry.Clear.fixedY);
        base.BeforeCalcBounds();
        ClearHint.BeforeCalcBounds();
    }

    public override void ComposeTextElements(Context ctx, ImageSurface surface)
    {
        FieldBounds.CalcWorldBounds();
        ClearBounds.CalcWorldBounds();
        using (var scratch = new ImageSurface(Format.Argb32, 1, 1))
        using (var native = new Context(scratch))
            base.ComposeTextElements(native, scratch);
        highlightBounds = FieldBounds;
        int width = Math.Max(1, (int)Math.Round(FieldBounds.OuterWidth, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(FieldBounds.OuterHeight, MidpointRounding.AwayFromZero));
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.SearchFill,
            GuiControlPalette.SearchOutline, scaled(1), ref idleFrame);
        GuiControlDrawing.FrameTexture(api, width, height, GuiControlPalette.SearchFill,
            GuiControlPalette.SelectedOutline, scaled(1), ref hoverFrame);
        ComposePlaceholder(width, height, 1, ref placeholder);
        ComposePlaceholder(width, height, GuiControlPalette.DisabledOpacity, ref disabledPlaceholder);
        ComposeClear(GuiControlPalette.SearchFill, GuiControlPalette.SearchOutline, ref clear);
        ComposeClear(GuiControlPalette.HoverFill, GuiControlPalette.SelectedOutline, ref clearHover);
        ComposeClear(GuiControlPalette.TabFill, GuiControlPalette.SelectedOutline, ref clearDown);
    }

    private void ComposePlaceholder(int width, int height, double opacity, ref LoadedTexture texture)
    {
        using var image = new ImageSurface(Format.Argb32, width, height);
        using var context = new Context(image);
        var font = placeholderFont.Clone();
        font.Color[3] *= opacity;
        font.SetupContext(context);
        context.Rectangle(scaled(GuiControlMetrics.TextInset), 0, Bounds.OuterWidth, height);
        context.Clip();
        textUtil.DrawTextLine(context, font, placeholderText, scaled(GuiControlMetrics.TextInset),
            (height - font.GetFontExtents().Height) / 2);
        generateTexture(image, ref texture, linearMag: false);
    }

    private void ComposeClear(int fill, int outline, ref LoadedTexture texture)
    {
        int size = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.ClearSize, scaled(1)));
        using var image = new ImageSurface(Format.Argb32, size, size);
        using var ctx = new Context(image);
        GuiControlDrawing.Frame(ctx, 0, 0, size, size, fill, outline, scaled(1));
        ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.Placeholder));
        ctx.LineWidth = scaled(GuiControlMetrics.ClearStroke);
        double inset = scaled(GuiControlMetrics.ClearInset);
        ctx.MoveTo(inset, inset);
        ctx.LineTo(size - inset, size - inset);
        ctx.MoveTo(size - inset, inset);
        ctx.LineTo(inset, size - inset);
        ctx.Stroke();
        generateTexture(image, ref texture, linearMag: false);
    }

    public override bool IsPositionInside(int x, int y) =>
        FieldBounds.PointInside(x, y) && (InsideClipBounds == null || InsideClipBounds.PointInside(x, y));

    public override void RenderInteractiveElements(float deltaTime)
    {
        bool hovered = Enabled && IsPositionInside(api.Input.MouseX, api.Input.MouseY);
        api.Render.Render2DTexturePremultipliedAlpha((hovered ? hoverFrame : idleFrame).TextureId, FieldBounds);
        base.RenderInteractiveElements(deltaTime);
        if (!HasFocus && GetText().Length == 0)
            api.Render.Render2DTexturePremultipliedAlpha(
                (Enabled ? placeholder : disabledPlaceholder).TextureId, FieldBounds);
        bool overClear = ClearVisible && ClearBounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        MouseOverCursor = !Enabled ? null : overClear ? "linkselect" : "textselect";
        ClearHint.SetAutoDisplay(ClearVisible);
        if (!ClearVisible)
        {
            ClearHint.SetVisible(false);
            return;
        }
        api.Render.Render2DTexturePremultipliedAlpha(
            (clearPressed ? clearDown : overClear ? clearHover : clear).TextureId, ClearBounds);
        ClearHint.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        clearPressed = false;
        if (!Enabled || !IsPositionInside(args.X, args.Y)) return;
        if (ClearVisible && ClearBounds.PointInside(args.X, args.Y))
        {
            clearPressed = args.Button == EnumMouseButton.Left;
            args.Handled = true;
            return;
        }
        base.OnMouseDownOnElement(api, args);
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        bool pressed = clearPressed;
        clearPressed = false;
        if (args.Handled || args.Button != EnumMouseButton.Left || !pressed || !ClearVisible
            || !IsPositionInside(args.X, args.Y) || !ClearBounds.PointInside(args.X, args.Y)) return;
        args.Handled = true;
        SetValue("");
        api.Gui.PlaySound("menubutton_press");
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        if (args.Button == EnumMouseButton.Left) clearPressed = false;
    }

    public override void OnFocusLost()
    {
        clearPressed = false;
        base.OnFocusLost();
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        idleFrame.Dispose();
        hoverFrame.Dispose();
        placeholder.Dispose();
        disabledPlaceholder.Dispose();
        clear.Dispose();
        clearHover.Dispose();
        clearDown.Dispose();
        ClearHint.Dispose();
        base.Dispose();
    }
}
