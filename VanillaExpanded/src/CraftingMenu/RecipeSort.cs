using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaExpanded.CraftingMenu;

internal sealed class RecipeSort(Action changed)
{
    internal const string Default = "availability";
    internal ImmutableArray<IRecipeOrder> Orders { get; } = [new AvailabilityOrder(), new NameOrder()];
    private IRecipeOrder? selected;
    internal IRecipeOrder Selected => selected ?? Orders[0];

    internal void Select(string id)
    {
        var order = Orders.FirstOrDefault(order => order.Id == id)
            ?? throw new ArgumentException("Unknown crafting recipe order.", nameof(id));
        if (Selected == order) return;
        selected = order;
        changed();
    }
}

internal interface IRecipeOrder
{
    string Id { get; }
    string NameKey { get; }
    IOrderedEnumerable<CraftingRecipeSelection> Order(IEnumerable<CraftingRecipeSelection> selections);
}

internal sealed class AvailabilityOrder : IRecipeOrder
{
    public string Id => RecipeSort.Default;
    public string NameKey => "vanillaexpanded:craftingmenu-sort-availability";

    public IOrderedEnumerable<CraftingRecipeSelection> Order(IEnumerable<CraftingRecipeSelection> selections) =>
        selections.OrderByDescending(selection => selection.Evaluation.IsSupported)
            .ThenByDescending(selection => selection.Evaluation.CanCraft)
            .ThenByDescending(selection => selection.Evaluation.Relevance)
            .ThenBy(selection => selection.Evaluation.MissingUnits)
            .ThenBy(selection => selection.Recipe.DisplayName, StringComparer.CurrentCultureIgnoreCase);
}

internal sealed class NameOrder : IRecipeOrder
{
    public string Id => "name";
    public string NameKey => "vanillaexpanded:craftingmenu-sort-name";

    public IOrderedEnumerable<CraftingRecipeSelection> Order(IEnumerable<CraftingRecipeSelection> selections) =>
        selections.OrderBy(selection => selection.Recipe.DisplayName, StringComparer.CurrentCultureIgnoreCase);
}
