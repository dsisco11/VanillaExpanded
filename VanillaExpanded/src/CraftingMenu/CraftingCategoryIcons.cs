using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal static class CraftingCategoryIcons
{
    internal static readonly ImmutableArray<string> Codes =
        ["all", "construction", "decorative", "mechanics", "terrain", "tools", "clothing", "items", "other", "mod-category"];

    internal static string ForCategory(string? code) => Id(code switch
    {
        null => "all",
        CraftingCatalogue.OtherCategory => "other",
        "construction" or "decorative" or "mechanics" or "terrain" or "tools" or "clothing" or "items" => code,
        _ => "mod-category"
    });

    internal static string Id(string code) => "vanillaexpanded-crafting-category-" + code;
    internal static AssetLocation Asset(string code) => new($"vanillaexpanded:textures/gui/crafting-categories/{code}.svg");
}
