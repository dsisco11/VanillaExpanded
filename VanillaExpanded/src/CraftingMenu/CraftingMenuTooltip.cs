using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal sealed record CraftingIngredientSummary(string Name, int Have, int Need);

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

    internal static ImmutableArray<CraftingIngredientSummary> IngredientSummary(CraftingRecipeSelection selection,
        System.Func<string, string>? translate = null)
    {
        if (!selection.Evaluation.IsSupported || selection.Allocation == null) return [];
        translate ??= key => Lang.Get(key);
        var missing = selection.Evaluation.Missing.ToDictionary(requirement => requirement.IngredientId);
        return selection.Allocation.Requirements.Select(requirement =>
        {
            var ingredient = requirement.GetIngredient();
            string name = ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.ResolvedItemStack is { } stack
                ? stack.GetName() : translate("vanillaexpanded:craftingmenu-ingredient-choice");
            int have = requirement.Quantity - (missing.GetValueOrDefault(requirement.Id)?.Quantity ?? 0);
            return new CraftingIngredientSummary(name, have, requirement.Quantity);
        }).ToImmutableArray();
    }

    internal static RichTextComponentBase[] CreateRecipe(ICoreClientAPI api, CraftingRecipeSelection selection, double width,
        ImmutableArray<CraftingIngredientSummary> ingredients)
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
        foreach (var ingredient in ingredients)
        {
            components.Add(new RichTextComponent(api, ingredient.Name + " ", CairoFont.WhiteSmallText()));
            var font = CairoFont.WhiteSmallText();
            if (ingredient.Have < ingredient.Need) font.WithColor((double[])GuiStyle.ErrorTextColor.Clone());
            components.Add(new RichTextComponent(api, $"{ingredient.Have}/{ingredient.Need}", font));
            components.Add(new ClearFloatTextComponent(api, 2));
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
