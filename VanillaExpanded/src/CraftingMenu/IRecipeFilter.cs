namespace VanillaExpanded.CraftingMenu;

/// <summary>Matches evaluated selections without changing them or reading live inventory or GUI state.</summary>
internal interface IRecipeFilter
{
    bool Matches(CraftingRecipeSelection selection);
}
