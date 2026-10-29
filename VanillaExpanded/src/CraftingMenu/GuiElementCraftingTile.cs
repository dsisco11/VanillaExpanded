using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal enum CraftingTileStatus { Available, MissingIngredients, Unsupported }

internal sealed class GuiElementCraftingTile : GuiElementTextButton
{
    private readonly Action<int, bool> activate;
    private readonly Action<GuiElementCraftingTile> focus;
    private readonly Action inspect;
    private CraftingRecipeSelection? selection;

    internal GuiElementCraftingTile(ICoreClientAPI api, ElementBounds bounds,
        Action<int, bool> activate,
        Action<GuiElementCraftingTile> focus, Action inspect)
        : base(api, "", CairoFont.WhiteSmallText(), CairoFont.WhiteSmallText(), () => true, bounds)
    {
        this.activate = activate;
        this.focus = focus;
        this.inspect = inspect;
    }

    internal CraftingRecipeSelection? Selection
    {
        get => selection;
        set
        {
            selection = value;
            Visible = value != null;
            if (!Visible && HasFocus) OnFocusLost();
        }
    }

    internal bool Pressed { get; private set; }
    internal CraftingTileStatus Status => selection?.Evaluation.IsSupported != true ? CraftingTileStatus.Unsupported
        : selection.Evaluation.CanCraft ? CraftingTileStatus.Available : CraftingTileStatus.MissingIngredients;
    internal bool IsInViewport => Visible && (InsideClipBounds is not { } clip ||
        (Bounds.absX < clip.absX + clip.OuterWidth && Bounds.absX + Bounds.OuterWidth > clip.absX
            && Bounds.absY < clip.absY + clip.OuterHeight && Bounds.absY + Bounds.OuterHeight > clip.absY));
    public override bool Focusable => Visible && base.Focusable;
    // Image-only buttons keep their tile dimensions instead of autosizing to text.
    public override void BeforeCalcBounds() { }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!IsInViewport) return;
        base.RenderInteractiveElements(deltaTime);
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
