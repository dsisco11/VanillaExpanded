using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal sealed class GuiElementCraftingSearch : GuiElementTextInput
{
    private const int ClearSize = 16;
    private const int ClearInset = 5;
    private LoadedTexture clearTexture;
    private bool clearPressed;
    internal ElementBounds FieldBounds { get; }
    internal ElementBounds ClearBounds { get; }
    internal GuiElementHoverText ClearHint { get; }
    internal bool ClearVisible => Enabled && GetText().Length > 0;
    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            if (!value) clearPressed = false;
            base.Enabled = value;
        }
    }

    internal GuiElementCraftingSearch(ICoreClientAPI api, ElementBounds bounds, Action<string> changed)
        : base(api, bounds.FlatCopy().WithFixedWidth(bounds.fixedWidth - ClearSize - 2 * ClearInset),
            changed, CairoFont.WhiteSmallText())
    {
        FieldBounds = bounds;
        ClearBounds = ElementBounds.Fixed(bounds.fixedX + bounds.fixedWidth - ClearSize - ClearInset,
            bounds.fixedY + (bounds.fixedHeight - ClearSize) / 2, ClearSize, ClearSize);
        clearTexture = new(api);
        ClearHint = new(api, Lang.Get("vanillaexpanded:craftingmenu-clear-search"),
            CairoFont.WhiteSmallText(), 160, ClearBounds);
        OnTextChanged = text =>
        {
            clearPressed = false;
            ClearHint.SetAutoDisplay(Enabled && text.Length > 0);
            if (text.Length == 0) ClearHint.SetVisible(false);
            changed(text);
        };
    }

    public override void BeforeCalcBounds()
    {
        FieldBounds.ParentBounds = Bounds.ParentBounds;
        ClearBounds.ParentBounds = Bounds.ParentBounds;
        base.BeforeCalcBounds();
        ClearHint.BeforeCalcBounds();
    }

    public override void ComposeTextElements(Context ctx, ImageSurface surface)
    {
        FieldBounds.CalcWorldBounds();
        ClearBounds.CalcWorldBounds();
        // Keep native text/highlight composition without drawing its narrower frame.
        using (var nativeSurface = new ImageSurface(Format.Argb32, 1, 1))
        using (var nativeContext = genContext(nativeSurface))
            base.ComposeTextElements(nativeContext, nativeSurface);

        EmbossRoundRectangleElement(ctx, FieldBounds, true, 2, 1);
        ctx.SetSourceRGBA(0, 0, 0, 0.2);
        ElementRoundRectangle(ctx, FieldBounds, false, 1);
        ctx.Fill();

        highlightBounds = FieldBounds.CopyOffsetedSibling().WithFixedPadding(0, 0)
            .FixedGrow(2 * FieldBounds.absPaddingX, 2 * FieldBounds.absPaddingY);
        highlightBounds.CalcWorldBounds();

        using var icon = new ImageSurface(Format.Argb32, (int)scaled(ClearSize), (int)scaled(ClearSize));
        using var iconContext = genContext(icon);
        double center = scaled(ClearSize / 2.0);
        iconContext.SetSourceRGBA(0.65, 0.65, 0.65, 1);
        iconContext.Arc(center, center, center - scaled(0.5), 0, 2 * Math.PI);
        iconContext.Fill();
        iconContext.SetSourceRGBA(0.12, 0.12, 0.12, 1);
        iconContext.LineWidth = scaled(1.5);
        iconContext.LineCap = LineCap.Round;
        double inset = scaled(5);
        iconContext.MoveTo(inset, inset);
        iconContext.LineTo(scaled(ClearSize) - inset, scaled(ClearSize) - inset);
        iconContext.MoveTo(scaled(ClearSize) - inset, inset);
        iconContext.LineTo(inset, scaled(ClearSize) - inset);
        iconContext.Stroke();
        generateTexture(icon, ref clearTexture);
    }

    public override bool IsPositionInside(int posX, int posY) =>
        FieldBounds.PointInside(posX, posY) && (InsideClipBounds == null || InsideClipBounds.PointInside(posX, posY));

    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);
        bool overClear = ClearVisible && ClearBounds.PointInside(api.Input.MouseX, api.Input.MouseY);
        MouseOverCursor = !Enabled ? null : overClear ? "linkselect" : "textselect";
        ClearHint.SetAutoDisplay(ClearVisible);
        if (!ClearVisible)
        {
            ClearHint.SetVisible(false);
            return;
        }
        api.Render.Render2DTexturePremultipliedAlpha(clearTexture.TextureId, ClearBounds);
        ClearHint.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        clearPressed = false;
        if (!Enabled) return;
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
            || !ClearBounds.PointInside(args.X, args.Y)) return;
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
        clearTexture.Dispose();
        ClearHint.Dispose();
        base.Dispose();
    }
}
