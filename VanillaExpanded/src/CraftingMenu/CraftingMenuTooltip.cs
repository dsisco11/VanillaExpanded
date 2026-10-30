using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal static class CraftingMenuTooltip
{
    internal static RichTextComponent CreateTitle(ICoreClientAPI api, CraftingRecipeSelection selection)
    {
        string title = selection.Recipe.DisplayName;
        if (selection.Recipe.OutputQuantity > 1) title += $" (x{selection.Recipe.OutputQuantity})";
        return new RichTextComponent(api, title, CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)
            .WithOrientation(EnumTextOrientation.Center));
    }

    internal static RichTextComponentBase[] CreateDescription(ICoreClientAPI api, CraftingRecipeSelection selection)
    {
        if (selection.Recipe.GetPreview() is not { } preview) return [];
        var inventory = new DummyInventory(api);
        inventory.OnAcquireTransitionSpeed += (_, _, _) => 0;
        var slot = new DummySlot(preview, inventory);
        string description = slot.GetStackDescription(api.World, false).TrimEnd();
        return string.IsNullOrWhiteSpace(description)
            ? [] : VtmlUtil.Richtextify(api, description, CairoFont.WhiteSmallText());
    }

    internal static RichTextComponentBase[] CreateRecipe(ICoreClientAPI api, CraftingRecipeSelection selection, double width)
    {
        var components = new List<RichTextComponentBase>();
        if (selection.Recipe.Recipe.ResolvedIngredients != null)
        {
            var grid = new SlideshowGridRecipeTextComponent(api,
                [selection.Recipe.Recipe.Clone()], 40, EnumFloat.Inline);
            // Center the visible cells before native layout adds trailing spacing.
            grid.PaddingLeft = Math.Max(0, (width - grid.BoundsPerLine[0].Width / GuiElement.scaled(1)) / 2);
            components.Add(grid);
            components.Add(new ClearFloatTextComponent(api, 6));
        }
        string warnings = Warnings(selection);
        if (warnings.Length > 0)
            components.Add(new RichTextComponent(api, warnings, CairoFont.WhiteSmallText()));
        return components.ToArray();
    }

    internal static string Warnings(CraftingRecipeSelection selection, System.Func<string, string>? translate = null)
    {
        translate ??= key => Lang.Get(key);
        return selection.Evaluation.IsSupported ? string.Empty : translate(selection.Evaluation.UnsupportedReason!);
    }
}
