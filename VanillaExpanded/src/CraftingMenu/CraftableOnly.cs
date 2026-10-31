using System;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftableOnly(RecipeFilterSet filters, Action changed)
{
    private readonly CraftableFilter filter = new();
    private RecipeFilterSet.Registration? registration;

    internal bool Enabled => registration != null;

    internal void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        if (enabled)
            registration = filters.Register("vanillaexpanded:craftable-only", filter);
        else
        {
            registration!.Dispose();
            registration = null;
        }
        changed();
    }
}

internal sealed class CraftableFilter : IRecipeFilter
{
    public bool Matches(CraftingRecipeSelection selection) => selection.Evaluation.CanCraft;
}
