using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementItemStackListMenu : GuiElementListMenu
{
    private readonly ItemSlot?[] iconSlots;

    internal GuiElementItemStackListMenu(
        ICoreClientAPI capi,
        string[] values,
        string[] names,
        ItemStack?[] icons,
        int selectedIndex,
        SelectionChangedDelegate onSelectionChanged,
        ElementBounds bounds,
        CairoFont font)
        : base(capi, values, names, selectedIndex, onSelectionChanged, bounds, font, false)
    {
        iconSlots = [.. icons.Select(static icon => icon is null ? null : new DummySlot(icon))];
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);
        if (!IsOpened) return;

        double scaleMultiplier = Scale * RuntimeEnv.GUIScale;
        double lineHeight = unscaledLineHeight * scaleMultiplier;
        double iconSize = scaled(GuiElementItemStackDropDown.IconSize) * Scale;
        double iconX = Bounds.renderX + scaled(GuiElementItemStackDropDown.IconCenterX) * Scale;

        api.Render.PushScissor(visibleBounds);
        for (var index = 0; index < iconSlots.Length; index++)
        {
            ItemSlot? slot = iconSlots[index];
            if (slot?.Itemstack is null) continue;

            double iconY = Bounds.renderY + Bounds.InnerHeight
                + (index + 0.5) * lineHeight - scrollOffY;
            api.Render.RenderItemstackToGui(
                slot,
                iconX,
                iconY,
                512,
                (float)iconSize,
                ColorUtil.WhiteArgb,
                showStackSize: false);
        }
        api.Render.PopScissor();
    }
}