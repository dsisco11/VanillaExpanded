namespace VanillaExpanded.RecipeBook;

internal sealed record RecipeBookBrowsingState(string? Category = null, string Search = "", float Scroll = 0,
    string Sort = RecipeSort.Default);
