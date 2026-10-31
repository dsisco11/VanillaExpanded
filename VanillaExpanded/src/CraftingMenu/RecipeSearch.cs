using System;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaExpanded.CraftingMenu;

internal sealed class RecipeSearch(RecipeFilterSet filters, Action changed)
{
    private RecipeFilterSet.Registration? registration;

    internal void SetText(string? text)
    {
        var filter = new SearchFilter(text);
        if (filter.IsEmpty)
        {
            registration?.Dispose();
            registration = null;
        }
        else if (registration == null)
            registration = filters.Register("vanillaexpanded:search", filter);
        else
            registration.Replace(filter);
        changed();
    }
}

internal sealed class SearchFilter : IRecipeFilter
{
    private readonly ImmutableArray<string> words;

    internal SearchFilter(string? text)
    {
        words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(static word => word.ToUpperInvariant()).ToImmutableArray();
    }

    internal bool IsEmpty => words.IsEmpty;

    public bool Matches(CraftingRecipeSelection selection) =>
        words.All(word => selection.Recipe.SearchText.Contains(word, StringComparison.Ordinal));
}
