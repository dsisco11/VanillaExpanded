using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaExpanded.RecipeBook;

internal sealed record CraftingRecipeSelection(
    CraftingOutputGroup Output,
    CraftingRecipeEntry Recipe,
    CraftingRecipeEvaluation Evaluation,
    bool IsExplicit,
    CraftingAllocation? Allocation = null)
{
    internal bool CanPlace => Evaluation.IsSupported;
    internal int GridMatches => Evaluation.IsSupported ? Allocation?.GridMatches ?? 0 : 0;
}

internal sealed class CraftingCatalogueView
{
    private static readonly CraftingRecipeEvaluation PendingEvaluation =
        CraftingRecipeEvaluation.Unsupported("vanillaexpanded:recipebook-evaluation-pending");
    private readonly CraftingCatalogue catalogue;
    private readonly Dictionary<int, int> pinnedRecipes = new();
    private Dictionary<int, CraftingRecipeEvaluation> evaluations = new();
    private Dictionary<int, CraftingAllocation> allocations = new();

    internal CraftingCatalogueView(CraftingCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        this.catalogue = catalogue;
    }

    internal void ReplaceEvaluations(IReadOnlyDictionary<int, CraftingRecipeEvaluation> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var validIds = catalogue.Recipes.Select(entry => entry.Id).ToHashSet();
        var replacement = new Dictionary<int, CraftingRecipeEvaluation>();
        foreach (var pair in results)
        {
            if (!validIds.Contains(pair.Key)) throw new ArgumentException("Unknown recipe identity.", nameof(results));
            ArgumentNullException.ThrowIfNull(pair.Value);
            replacement.Add(pair.Key, pair.Value);
        }
        evaluations = replacement;
        allocations.Clear();
    }

    internal void ReplaceAllocations(IReadOnlyDictionary<int, CraftingAllocation> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var replacement = new Dictionary<int, CraftingAllocation>();
        foreach (var pair in results)
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            replacement.Add(pair.Key, pair.Value);
        }
        ReplaceEvaluations(replacement.ToDictionary(pair => pair.Key, pair => pair.Value.Evaluation));
        allocations = replacement;
    }

    internal void SelectAlternative(int outputId, int recipeId)
    {
        var group = GetGroup(outputId);
        if (!group.Alternatives.Any(entry => entry.Id == recipeId))
            throw new ArgumentException("The recipe does not belong to this output.", nameof(recipeId));
        pinnedRecipes[outputId] = recipeId;
    }

    internal void SelectAuto(int outputId)
    {
        GetGroup(outputId);
        pinnedRecipes.Remove(outputId);
    }

    internal void Deselect(int outputId) => SelectAuto(outputId);

    internal CraftingRecipeSelection GetSelection(int outputId, IRecipeOrder? order = null)
    {
        var group = GetGroup(outputId);
        if (pinnedRecipes.TryGetValue(outputId, out int pinned))
        {
            var selected = group.Alternatives.First(entry => entry.Id == pinned);
            return new(group, selected, GetEvaluation(selected), true, allocations.GetValueOrDefault(selected.Id));
        }
        order ??= new AvailabilityOrder();
        return order.OrderAlternatives(group.Alternatives.Select(entry =>
                new CraftingRecipeSelection(group, entry, GetEvaluation(entry), false, allocations.GetValueOrDefault(entry.Id))))
            .ThenBy(selection => selection.Recipe.StableId, StringComparer.Ordinal)
            .First();
    }

    internal ImmutableArray<CraftingRecipeSelection> GetRows(RecipeFilterSet filters, IRecipeOrder? order = null)
    {
        ArgumentNullException.ThrowIfNull(filters);
        order ??= new AvailabilityOrder();
        return order.Order(filters.Filter(catalogue.Groups.Select(group => GetSelection(group.Id, order))))
            .ThenBy(selection => selection.Recipe.StableId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal CraftingRecipeSelection RefreshSelection(CraftingRecipeSelection selection) =>
        selection with
        {
            Evaluation = GetEvaluation(selection.Recipe),
            Allocation = allocations.GetValueOrDefault(selection.Recipe.Id)
        };

    private CraftingRecipeEvaluation GetEvaluation(CraftingRecipeEntry entry) =>
        entry.UnresolvedReason != null ? CraftingRecipeEvaluation.Unsupported(entry.UnresolvedReason)
        : evaluations.TryGetValue(entry.Id, out var evaluation) ? evaluation
        : PendingEvaluation;

    private CraftingOutputGroup GetGroup(int outputId)
    {
        if (outputId < 0 || outputId >= catalogue.Groups.Length)
            throw new ArgumentOutOfRangeException(nameof(outputId));
        return catalogue.Groups[outputId];
    }
}
