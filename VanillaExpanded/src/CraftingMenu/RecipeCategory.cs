using System;
using System.Linq;

namespace VanillaExpanded.CraftingMenu;

internal sealed class RecipeCategory(CraftingCatalogue catalogue, RecipeFilterSet filters, Action changed)
{
    private RecipeFilterSet.Registration? registration;
    internal string? Selected { get; private set; }

    internal void Select(string? category)
    {
        if (category != null && !catalogue.CategoryCodes.Contains(category))
            throw new ArgumentException("Unknown crafting category.", nameof(category));
        if (Selected == category) return;
        if (category == null)
        {
            registration?.Dispose();
            registration = null;
        }
        else if (registration == null)
            registration = filters.Register("vanillaexpanded:category", new CategoryFilter(category));
        else
            registration.Replace(new CategoryFilter(category));
        Selected = category;
        changed();
    }
}

internal sealed class CategoryFilter : IRecipeFilter
{
    private readonly string category;

    internal CategoryFilter(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        this.category = category;
    }

    public bool Matches(CraftingRecipeSelection selection) => category == CraftingCatalogue.OtherCategory
        ? selection.Recipe.CategoryCodes.IsEmpty : selection.Recipe.CategoryCodes.Contains(category);
}
