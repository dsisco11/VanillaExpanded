using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.CraftingMenu;

internal static class CraftingMenuTooltip
{
    internal static string Describe(CraftingRecipeSelection selection, System.Func<string, string>? translate = null,
        System.Func<ItemStack, string>? getName = null,
        System.Func<TagSet, IEnumerable<string>>? getTagNames = null)
    {
        translate ??= key => Lang.Get(key);
        getName ??= stack => stack.GetName();
        var lines = new List<string> { selection.Recipe.DisplayName + " x" + selection.Recipe.OutputQuantity };
        if (selection.Recipe.IsGenericPreview) lines.Add(translate("vanillaexpanded:craftingmenu-generic"));
        lines.Add(translate("vanillaexpanded:craftingmenu-ingredients"));
        foreach (var ingredient in selection.Recipe.Ingredients.Where(ingredient => ingredient != null))
        {
            var consume = ingredient!.GetConsumeProperties();
            lines.Add($"{(consume.Consume ? ingredient.Quantity : 1)} x {Ingredient(ingredient, translate, getName, getTagNames)}"
                + (consume.Consume ? "" : $" ({translate("vanillaexpanded:craftingmenu-tool")}, {consume.DurabilityCost})"));
        }
        if (!selection.Evaluation.IsSupported)
        {
            lines.Add(translate("vanillaexpanded:craftingmenu-unknown"));
            lines.Add(translate(selection.Evaluation.UnsupportedReason!));
        }
        else
        {
            lines.Add(translate(selection.Evaluation.CanCraft ? "vanillaexpanded:craftingmenu-available"
                : "vanillaexpanded:craftingmenu-unavailable"));
            foreach (var deficit in selection.Evaluation.Missing)
            {
                var requirement = selection.Allocation!.Requirements.First(need => need.Id == deficit.IngredientId);
                lines.Add($"{translate("vanillaexpanded:craftingmenu-missing")} {deficit.Quantity} x "
                    + Ingredient(requirement.GetIngredient(), translate, getName, getTagNames)
                    + (deficit.Reason == null ? "" : " - " + translate(deficit.Reason)));
            }
        }
        lines.Add(translate("vanillaexpanded:craftingmenu-hints"));
        return string.Join("\n", lines);
    }

    private static string Ingredient(CraftingRecipeIngredient ingredient, System.Func<string, string> translate,
        System.Func<ItemStack, string> getName, System.Func<TagSet, IEnumerable<string>>? getTagNames)
    {
        string name = ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.ResolvedItemStack != null
            ? getName(ingredient.ResolvedItemStack) : ingredient.Code?.ToString() ?? ingredient.Id;
        if (ingredient.AllowedVariants is { Length: > 0 })
            name += " (" + translate("vanillaexpanded:craftingmenu-oneof") + " "
                + string.Join(", ", ingredient.AllowedVariants) + ")";
        if (ingredient.SkipVariants is { Length: > 0 })
            name += " (" + translate("vanillaexpanded:craftingmenu-except") + " "
                + string.Join(", ", ingredient.SkipVariants) + ")";
        if (ingredient.Attributes != null) name += " " + ingredient.Attributes;
        if (ingredient.MatchingType != EnumRecipeMatchType.Exact && ingredient.Tags.conditions != null)
            name += " " + Tags(ingredient.Tags, translate, getTagNames);
        return name;
    }

    private static string Tags(ComplexTagCondition<TagSet> tags, System.Func<string, string> translate,
        System.Func<TagSet, IEnumerable<string>>? getTagNames)
    {
        if (tags.conditions!.Length == 0)
            return tags.isDisjunctive ? translate("vanillaexpanded:craftingmenu-tags-impossible") : "";
        ArgumentNullException.ThrowIfNull(getTagNames);
        var choices = tags.conditions.Select(condition =>
        {
            string required = condition.RequiredTags.IsEmpty
                ? translate(tags.isDisjunctive ? "vanillaexpanded:craftingmenu-tags-unrestricted"
                    : "vanillaexpanded:craftingmenu-tags-impossible")
                : translate(tags.isDisjunctive ? "vanillaexpanded:craftingmenu-tags-all"
                    : "vanillaexpanded:craftingmenu-tags-any") + " "
                    + string.Join(", ", getTagNames(condition.RequiredTags).Order(StringComparer.Ordinal));
            string forbidden = condition.ForbiddenTags.IsEmpty ? ""
                : "; " + translate("vanillaexpanded:craftingmenu-tags-none") + " "
                    + string.Join(", ", getTagNames(condition.ForbiddenTags).Order(StringComparer.Ordinal));
            return "(" + required + forbidden + ")";
        });
        return translate("vanillaexpanded:craftingmenu-tags") + " "
            + string.Join(" " + translate(tags.isDisjunctive
                ? "vanillaexpanded:craftingmenu-or" : "vanillaexpanded:craftingmenu-and") + " ", choices);
    }
}
