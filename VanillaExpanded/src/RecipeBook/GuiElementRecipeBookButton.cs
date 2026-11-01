using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.RecipeBook;

// Native icon buttons do not hide or suppress activation when disabled.
internal sealed class GuiElementRecipeBookButton(ICoreClientAPI api, string icon,
    Action<bool> onClick, ElementBounds bounds)
    : GuiElementToggleButton(api, icon, "", CairoFont.WhiteDetailText(), null, bounds, true)
{
    private bool mousePressed;
    private bool previousValue;
    internal GuiElementHoverText Hint { get; } = new(api, Lang.Get("vanillaexpanded:recipebook-toggle"),
        CairoFont.WhiteSmallText(), 160, bounds);
    private bool visible = true;
    internal bool Visible
    {
        get => visible;
        set
        {
            visible = value;
            Hint.SetAutoDisplay(value && Enabled);
            if (!value)
            {
                OnFocusLost();
                SetValue(false);
                Hint.SetVisible(false);
            }
        }
    }

    public override bool Focusable => Visible && base.Focusable;

    public override void BeforeCalcBounds()
    {
        base.BeforeCalcBounds();
        Hint.BeforeCalcBounds();
    }

    public override bool IsPositionInside(int posX, int posY) =>
        Visible && Enabled && base.IsPositionInside(posX, posY);

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!Visible || !Enabled) return;
        base.RenderInteractiveElements(deltaTime);
        Hint.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!Visible || !Enabled || args.Button != EnumMouseButton.Left) return;
        previousValue = On;
        mousePressed = true;
        base.OnMouseDownOnElement(api, args);
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        if (!mousePressed || args.Button != EnumMouseButton.Left) return;
        mousePressed = false;
        if (Visible && Enabled && IsPositionInside(args.X, args.Y)) onClick(On);
        else SetValue(previousValue);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!Visible || !Enabled || !HasFocus || args.Handled || mousePressed) return;
        base.OnKeyDown(api, args);
        if (args.Handled && args.KeyCode == (int)GlKeys.Enter) onClick(On);
    }

    public override void OnFocusLost()
    {
        if (mousePressed)
        {
            mousePressed = false;
            SetValue(previousValue);
        }
        base.OnFocusLost();
    }

    public override void Dispose()
    {
        Hint.Dispose();
        base.Dispose();
    }
}
