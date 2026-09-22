using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementItemStackDropDown : GuiElementDropDown
{
    internal const double IconSize = 16;
    internal const double IconCenterX = 15;
    private const string LabelIndent = "      ";
    private readonly ItemSlot?[] iconSlots;

    internal GuiElementItemStackDropDown(
        ICoreClientAPI capi,
        string[] values,
        string[] names,
        ItemStack?[] icons,
        int selectedIndex,
        SelectionChangedDelegate onSelectionChanged,
        ElementBounds bounds,
        CairoFont font)
        : base(capi, values, PadNames(names), selectedIndex, onSelectionChanged, bounds, font, false)
    {
        iconSlots = [.. icons.Select(static icon => icon is null ? null : new DummySlot(icon))];

        listMenu.Dispose();
        listMenu = new GuiElementItemStackListMenu(
            capi,
            values,
            PadNames(names),
            icons,
            selectedIndex,
            OnSelectionChanged,
            bounds.ForkChildOffseted(-bounds.fixedX, -bounds.fixedY).WithAlignment(EnumDialogArea.None),
            font)
        {
            HoveredIndex = selectedIndex
        };
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        base.RenderInteractiveElements(deltaTime);

        int selectedIndex = listMenu.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= iconSlots.Length) return;

        ItemSlot? slot = iconSlots[selectedIndex];
        if (slot?.Itemstack is null) return;

        double iconSize = scaled(IconSize) * Scale;
        api.Render.RenderItemstackToGui(
            slot,
            Bounds.renderX + scaled(IconCenterX) * Scale,
            Bounds.renderY + Bounds.InnerHeight / 2,
            512,
            (float)iconSize,
            ColorUtil.WhiteArgb,
            showStackSize: false);
    }

    private void OnSelectionChanged(string value, bool selected)
    {
        onSelectionChanged?.Invoke(value, selected);
        SetSelectedIndex(listMenu.SelectedIndex);
    }

    private static string[] PadNames(string[] names)
    {
        return [.. names.Select(static name => $"{LabelIndent}{name}")];
    }
}