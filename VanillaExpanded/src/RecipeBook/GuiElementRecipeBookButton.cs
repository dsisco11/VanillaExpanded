using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.RecipeBook;

// Native icon buttons do not hide or suppress activation when disabled.
internal sealed class GuiElementRecipeBookButton(ICoreClientAPI api, string icon,
    Action<bool> onClick, ElementBounds bounds)
    : GuiElementToggleButton(api, icon, "", CairoFont.WhiteDetailText(), onClick, bounds, true)
{
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
        if (Visible && Enabled) base.OnMouseDownOnElement(api, args);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (Visible && Enabled) base.OnKeyDown(api, args);
    }

    public override void Dispose()
    {
        Hint.Dispose();
        base.Dispose();
    }
}
