using System;
using System.Linq;
using Vintagestory.API.Client;

namespace VanillaExpanded.RecipeBook;

internal static class RecipeBookLayout
{
    internal const double PreferredWidth = RecipeBookVisualMetrics.PreferredWidth;
    internal const double PreferredHeight = RecipeBookVisualMetrics.PreferredHeight;
    private const double DockGapPixels = 2;

    internal static ElementBounds? Place(ElementBounds parent, ElementBounds inputs,
        ElementBounds output, ElementBounds window, double minimumWidth, double minimumHeight)
    {
        double scale = GuiElement.scaled(1);
        double width = window.InnerWidth / scale;
        double height = window.InnerHeight / scale;
        double px = parent.absX / scale;
        double py = parent.absY / scale;
        double right = parent.absX + parent.OuterWidth;
        double gridLeft = Math.Min(inputs.absX, output.absX);
        double gridTop = Math.Min(inputs.absY, output.absY);
        double gridRight = Math.Max(inputs.absX + inputs.OuterWidth, output.absX + output.OuterWidth);
        double gridBottom = Math.Max(inputs.absY + inputs.OuterHeight, output.absY + output.OuterHeight);
        // Subtract docking extents in screen pixels before converting to GUI units.
        var spaces = new[]
        {
            ElementBounds.Fixed((right + DockGapPixels) / scale, 0,
                (window.InnerWidth - right - DockGapPixels) / scale, height),
            ElementBounds.Fixed(0, 0, (parent.absX - DockGapPixels) / scale, height),
            ElementBounds.Fixed(0, 0, (gridLeft - DockGapPixels) / scale, height),
            ElementBounds.Fixed((gridRight + DockGapPixels) / scale, 0,
                (window.InnerWidth - gridRight - DockGapPixels) / scale, height),
            ElementBounds.Fixed(0, 0, width, (gridTop - DockGapPixels) / scale),
            ElementBounds.Fixed(0, (gridBottom + DockGapPixels) / scale,
                width, (window.InnerHeight - gridBottom - DockGapPixels) / scale)
        };
        foreach (var space in spaces.Where(space => space.fixedWidth >= minimumWidth && space.fixedHeight >= minimumHeight))
        {
            double w = Math.Min(Math.Max(PreferredWidth, minimumWidth), space.fixedWidth);
            double h = Math.Min(Math.Max(PreferredHeight, minimumHeight), space.fixedHeight);
            double x = Math.Clamp(px < space.fixedX ? space.fixedX : space.fixedX + space.fixedWidth - w, 0, width - w);
            double y = Math.Clamp(py, space.fixedY, space.fixedY + space.fixedHeight - h);
            return ElementBounds.Fixed(x, y, w, h).WithParent(window);
        }
        return null;
    }

    internal static ElementBounds? PlaceMovable(ElementBounds bounds, ElementBounds window,
        double minimumWidth, double minimumHeight)
    {
        double scale = GuiElement.scaled(1);
        double width = window.InnerWidth / scale;
        double height = window.InnerHeight / scale;
        if (width < minimumWidth || height < minimumHeight) return null;
        double w = Math.Clamp(bounds.fixedWidth, minimumWidth, width);
        double h = Math.Clamp(bounds.fixedHeight, minimumHeight, height);
        return ElementBounds.Fixed(Math.Clamp(bounds.fixedX, 0, width - w),
            Math.Clamp(bounds.fixedY, 0, height - h), w, h).WithParent(window);
    }
}
