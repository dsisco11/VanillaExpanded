using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingRecipeEntry
{
    private readonly ItemStack? preview;

    internal CraftingRecipeEntry(int id, GridRecipe recipe, string displayName)
    {
        Id = id;
        Recipe = recipe;
        StableId = $"{recipe.Name?.ToString() ?? recipe.Output?.Code?.ToString() ?? "recipe"}#{id:D8}";
        DisplayName = displayName;
        OutputCode = recipe.Output?.ResolvedItemStack?.Collectible.Code?.ToString()
            ?? recipe.Output?.Code?.ToString() ?? recipe.Name?.ToString() ?? StableId;
        OutputQuantity = recipe.Output?.Quantity ?? 0;
        preview = recipe.Output?.ResolvedItemStack?.Clone();
        if (preview != null) preview.StackSize = OutputQuantity;
        HasCustomIngredientPredicates = recipe.ResolvedIngredients?.Any(ingredient =>
            ingredient != null && ingredient.GetType() != typeof(CraftingRecipeIngredient)) ?? false;
        Ingredients = recipe.ResolvedIngredients?.Select(ingredient => ingredient?.Clone()).ToImmutableArray()
            ?? ImmutableArray<CraftingRecipeIngredient?>.Empty;
        IsGenericPreview = recipe.AverageDurability || recipe.CopyAttributesFrom != null || recipe.MergeAttributesFrom.Length > 0
            || recipe.GetType() != typeof(GridRecipe)
            || (preview != null && preview.Collectible.GetType() != typeof(Item)
                && preview.Collectible.GetType() != typeof(Block))
            || (preview?.Collectible.CollectibleBehaviors?.Length ?? 0) > 0;
        UnresolvedReason = recipe.ResolvedIngredients == null ? "vanillaexpanded:craftingmenu-unresolved-ingredients"
            : preview == null ? "vanillaexpanded:craftingmenu-unresolved-output" : null;
        SearchText = (displayName + "\n" + OutputCode).ToUpperInvariant();
    }

    internal int Id { get; }
    internal string StableId { get; }
    internal GridRecipe Recipe { get; }
    internal string DisplayName { get; }
    internal string OutputCode { get; }
    internal int OutputQuantity { get; }
    internal bool IsGenericPreview { get; }
    internal bool HasCustomIngredientPredicates { get; }
    internal string? UnresolvedReason { get; }
    internal ImmutableArray<CraftingRecipeIngredient?> Ingredients { get; }
    internal string SearchText { get; }
    internal ItemStack? GetPreview() => preview?.Clone();
    internal bool HasSameOutputAttributes(ItemStack stack, IWorldAccessor world) =>
        preview != null && preview.Attributes.Equals(world, stack.Attributes);

}

internal sealed record CraftingOutputGroup(int Id, ImmutableArray<CraftingRecipeEntry> Alternatives);

internal sealed class CraftingCatalogue
{
    private readonly Dictionary<GridRecipe, CraftingRecipeEntry> byRecipe;
    private readonly KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>>[] ingredientIndex;
    private readonly ImmutableArray<CraftingRecipeEntry> unindexed;

    private CraftingCatalogue(IWorldAccessor world, ImmutableArray<CraftingOutputGroup> groups)
    {
        Groups = groups;
        Recipes = groups.SelectMany(group => group.Alternatives).OrderBy(entry => entry.Id).ToImmutableArray();
        byRecipe = new Dictionary<GridRecipe, CraftingRecipeEntry>(ReferenceEqualityComparer.Instance);
        foreach (var entry in Recipes) byRecipe.Add(entry.Recipe, entry);
        ingredientIndex = world.FastSearchRecipesByIngredient.ToArray();
        var indexed = ingredientIndex.SelectMany(pair => pair.Value).OfType<GridRecipe>()
            .ToHashSet(ReferenceEqualityComparer.Instance);
        unindexed = Recipes.Where(entry => !indexed.Contains(entry.Recipe)).ToImmutableArray();
    }

    internal ImmutableArray<CraftingOutputGroup> Groups { get; }
    internal ImmutableArray<CraftingRecipeEntry> Recipes { get; }

    internal static CraftingCatalogue Build(
        IEnumerable<IRecipeBase> recipes,
        IWorldAccessor world,
        int gridWidth,
        int gridHeight,
        System.Func<GridRecipe, bool> canAccess,
        System.Func<ItemStack, string>? getDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(canAccess);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gridHeight);
        getDisplayName ??= stack => stack.GetName();

        var groups = new List<List<CraftingRecipeEntry>>();
        var buckets = new Dictionary<(EnumItemClass, int), List<int>>();
        var seen = new HashSet<GridRecipe>(ReferenceEqualityComparer.Instance);
        int id = 0;
        foreach (var source in recipes)
        {
            int entryId = id++;
            if (source is not GridRecipe recipe || !seen.Add(recipe) || !recipe.Enabled
                || !recipe.ShowInCreatedBy || recipe.Width <= 0 || recipe.Height <= 0
                || recipe.Width > gridWidth || recipe.Height > gridHeight || !canAccess(recipe)) continue;

            var output = recipe.Output?.ResolvedItemStack;
            string name = output != null ? getDisplayName(output)
                : recipe.Output?.Code?.ToString() ?? recipe.Name?.ToString() ?? $"recipe#{entryId:D8}";
            var entry = new CraftingRecipeEntry(entryId, recipe, name);
            int groupId = -1;
            if (output != null)
            {
                var key = (output.Class, output.Id);
                if (!buckets.TryGetValue(key, out var candidates))
                {
                    candidates = new List<int>();
                    buckets.Add(key, candidates);
                }
                foreach (int candidate in candidates)
                {
                    if (groups[candidate][0].HasSameOutputAttributes(output, world))
                    {
                        groupId = candidate;
                        break;
                    }
                }
                if (groupId < 0)
                {
                    groupId = groups.Count;
                    candidates.Add(groupId);
                    groups.Add(new List<CraftingRecipeEntry>());
                }
            }
            else
            {
                groupId = groups.Count;
                groups.Add(new List<CraftingRecipeEntry>());
            }
            groups[groupId].Add(entry);
        }

        return new CraftingCatalogue(world, groups.Select((entries, index) =>
            new CraftingOutputGroup(index, entries.OrderBy(entry => entry.StableId, StringComparer.Ordinal)
                .ToImmutableArray())).ToImmutableArray());
    }

    internal CraftingIngredientLookup CreateLookup() => new(ingredientIndex);

    internal ImmutableArray<CraftingRecipeEntry> FindCandidates(IEnumerable<ItemStack> eligibleStacks) =>
        FindCandidates(eligibleStacks, CreateLookup());

    internal ImmutableArray<CraftingRecipeEntry> FindCandidates(IEnumerable<ItemStack> eligibleStacks,
        CraftingIngredientLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(eligibleStacks);
        var stacks = eligibleStacks.Where(stack => stack.StackSize > 0).Select(CraftingStackSnapshot.Copy).ToArray();
        var candidates = new HashSet<int>();
        foreach (var stack in stacks)
            foreach (var recipe in lookup.Match(stack))
                if (recipe is GridRecipe grid && byRecipe.TryGetValue(grid, out var entry))
                    candidates.Add(entry.Id);

        foreach (var entry in Recipes)
        {
            // Custom predicates may not be represented by the native broad index.
            if (entry.Recipe.GetType() != typeof(GridRecipe)
                || entry.Ingredients.Length == 0 || entry.Ingredients.All(ingredient => ingredient == null)
                || entry.HasCustomIngredientPredicates)
            {
                candidates.Add(entry.Id);
            }
        }
        foreach (var entry in unindexed)
            if (entry.Ingredients.Any(ingredient =>
                ingredient != null && stacks.Any(stack => ingredient.SatisfiesAsIngredient(stack, false))))
                candidates.Add(entry.Id);
        return Recipes.Where(entry => candidates.Contains(entry.Id)).ToImmutableArray();
    }
}
