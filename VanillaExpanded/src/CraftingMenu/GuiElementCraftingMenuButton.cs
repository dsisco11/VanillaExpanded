using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

// Native icon buttons do not hide or suppress activation when disabled.
internal sealed class GuiElementCraftingMenuButton(ICoreClientAPI api, string icon,
    Action<bool> onClick, ElementBounds bounds)
    : GuiElementToggleButton(api, icon, "", CairoFont.WhiteDetailText(), onClick, bounds)
{
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (Enabled) base.RenderInteractiveElements(deltaTime);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (Enabled) base.OnMouseDownOnElement(api, args);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (Enabled) base.OnKeyDown(api, args);
    }
}
