namespace VanillaExpanded.CraftingMenu;

internal sealed record CraftingMenuBrowsingState(string? Category = null, string Search = "", float Scroll = 0,
    string Sort = RecipeSort.Default);
