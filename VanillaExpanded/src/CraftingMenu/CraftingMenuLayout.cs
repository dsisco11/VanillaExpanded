using System;
using System.Linq;
using Vintagestory.API.Client;

namespace VanillaExpanded.CraftingMenu;

internal static class CraftingMenuLayout
{
    internal static ElementBounds? Place(ElementBounds parent, ElementBounds inputs,
        ElementBounds output, ElementBounds window)
    {
        double scale = GuiElement.scaled(1);
        double width = window.InnerWidth / scale;
        double height = window.InnerHeight / scale;
        double px = parent.absX / scale;
        double py = parent.absY / scale;
        double right = (parent.absX + parent.OuterWidth) / scale;
        double gridLeft = Math.Min(inputs.absX, output.absX) / scale;
        double gridTop = Math.Min(inputs.absY, output.absY) / scale;
        double gridRight = Math.Max(inputs.absX + inputs.OuterWidth, output.absX + output.OuterWidth) / scale;
        double gridBottom = Math.Max(inputs.absY + inputs.OuterHeight, output.absY + output.OuterHeight) / scale;
        const double gap = 6;
        var spaces = new[]
        {
            ElementBounds.Fixed(right + gap, 0, width - right - gap, height),
            ElementBounds.Fixed(0, 0, px - gap, height),
            ElementBounds.Fixed(0, 0, gridLeft - gap, height),
            ElementBounds.Fixed(gridRight + gap, 0, width - gridRight - gap, height),
            ElementBounds.Fixed(0, 0, width, gridTop - gap),
            ElementBounds.Fixed(0, gridBottom + gap, width, height - gridBottom - gap)
        };
        foreach (var space in spaces.Where(space => space.fixedWidth >= 120 && space.fixedHeight >= 164))
        {
            double w = Math.Min(320, space.fixedWidth);
            double h = Math.Min(420, space.fixedHeight);
            double x = Math.Clamp(px < space.fixedX ? space.fixedX : space.fixedX + space.fixedWidth - w, 0, width - w);
            double y = Math.Clamp(py, space.fixedY, space.fixedY + space.fixedHeight - h);
            return ElementBounds.Fixed(x - px - parent.absPaddingX / scale,
                y - py - parent.absPaddingY / scale, w, h).WithParent(parent);
        }
        return null;
    }
}
