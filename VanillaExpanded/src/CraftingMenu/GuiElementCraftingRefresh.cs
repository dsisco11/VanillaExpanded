using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

// Runs before native slot-grid finalization consumes the output's rendering dirty flag.
internal sealed class GuiElementCraftingRefresh(ICoreClientAPI api, ElementBounds bounds,
    InventoryBase grid, Action invalidate) : GuiElement(api, bounds)
{
    public override double DrawOrder => -100;

    public override void PostRenderInteractiveElements(float deltaTime)
    {
        if (grid.DirtySlots.Contains(9)) invalidate();
    }
}
