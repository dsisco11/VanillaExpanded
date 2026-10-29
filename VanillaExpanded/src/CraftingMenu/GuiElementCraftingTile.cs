using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.CraftingMenu;

internal sealed class GuiElementCraftingTile : GuiElementTextButton
{
    private readonly IReadOnlyDictionary<string, LoadedTexture> badges;
    private readonly Action<int, bool> activate;
    private readonly Action<GuiElementCraftingTile> focus;
    private readonly Action inspect;
    private CraftingRecipeSelection? selection;
    private ItemSlot? icon;

    internal GuiElementCraftingTile(ICoreClientAPI api, ElementBounds bounds,
        IReadOnlyDictionary<string, LoadedTexture> badges, Action<int, bool> activate,
        Action<GuiElementCraftingTile> focus, Action inspect)
        : base(api, "", CairoFont.WhiteSmallText(), CairoFont.WhiteSmallText(), () => true, bounds)
    {
        this.badges = badges;
        this.activate = activate;
        this.focus = focus;
        this.inspect = inspect;
    }

    internal CraftingRecipeSelection? Selection
    {
        get => selection;
        set
        {
            if (selection?.Recipe != value?.Recipe)
                icon = value?.Recipe.GetPreview() is { } preview ? new DummySlot(preview) : null;
            selection = value;
            Visible = value != null;
            if (!Visible && HasFocus) OnFocusLost();
        }
    }

    internal bool Pressed { get; private set; }
    public override bool Focusable => Visible && base.Focusable;
    // Image-only buttons keep their tile dimensions instead of autosizing to text.
    public override void BeforeCalcBounds() { }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!Visible || (InsideClipBounds is { } clip &&
            (Bounds.absX >= clip.absX + clip.OuterWidth || Bounds.absX + Bounds.OuterWidth <= clip.absX
                || Bounds.absY >= clip.absY + clip.OuterHeight || Bounds.absY + Bounds.OuterHeight <= clip.absY))) return;
        base.RenderInteractiveElements(deltaTime);
        if (icon != null)
            api.Render.RenderItemstackToGui(icon, Bounds.renderX + Bounds.InnerWidth / 2,
                Bounds.renderY + Bounds.InnerHeight / 2, 220, (float)(Bounds.InnerWidth * .8),
                selection!.Evaluation.CanCraft ? ColorUtil.WhiteArgb : unchecked((int)0xff777777),
                showStackSize: true);
        if (!selection!.Evaluation.CanCraft) Badge(selection.Evaluation.IsSupported ? "!" : "?", Bounds.renderX);
        if (selection.Output.Alternatives.Length > 1) Badge("+", Bounds.renderX + Bounds.InnerWidth - scaled(14));
    }

    private void Badge(string text, double x)
    {
        var texture = badges[text];
        api.Render.Render2DTexture(texture.TextureId, (float)x, (float)Bounds.renderY,
            texture.Width, texture.Height, 250);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Button != EnumMouseButton.Left) return;
        base.OnMouseDownOnElement(api, args);
        Pressed = true;
        inspect();
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        Pressed = false;
    }

    public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
    {
        bool handled = args.Handled;
        base.OnMouseUpOnElement(api, args);
        if (!handled && args.Handled && args.Button == EnumMouseButton.Left && selection != null)
            activate(selection.Output.Id, api.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftLeft]
                || api.Input.KeyboardKeyStateRaw[(int)GlKeys.ShiftRight]);
    }

    public override void OnFocusGained()
    {
        base.OnFocusGained();
        focus(this);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!HasFocus || !Visible) return;
        if (args.KeyCode is (int)GlKeys.Space or (int)GlKeys.KeypadEnter)
        {
            args.Handled = true;
            activate(selection!.Output.Id, args.ShiftPressed);
            return;
        }
        base.OnKeyDown(api, args);
        if (args.Handled && args.KeyCode == (int)GlKeys.Enter)
            activate(selection!.Output.Id, args.ShiftPressed);
    }
}
