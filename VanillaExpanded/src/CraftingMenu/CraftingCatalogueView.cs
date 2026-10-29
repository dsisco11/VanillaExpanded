using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaExpanded.CraftingMenu;

internal sealed record CraftingRecipeSelection(
    CraftingOutputGroup Output,
    CraftingRecipeEntry Recipe,
    CraftingRecipeEvaluation Evaluation,
    bool IsExplicit)
{
    internal bool CanPlace => Evaluation.IsSupported;
}

internal sealed class CraftingCatalogueView
{
    private static readonly CraftingRecipeEvaluation PendingEvaluation =
        CraftingRecipeEvaluation.Unsupported("vanillaexpanded:craftingmenu-evaluation-pending");
    private readonly CraftingCatalogue catalogue;
    private readonly Dictionary<int, int> pinnedRecipes = new();
    private Dictionary<int, CraftingRecipeEvaluation> evaluations = new();

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

    internal CraftingRecipeSelection GetSelection(int outputId)
    {
        var group = GetGroup(outputId);
        if (pinnedRecipes.TryGetValue(outputId, out int pinned))
        {
            var selected = group.Alternatives.First(entry => entry.Id == pinned);
            return new(group, selected, GetEvaluation(selected), true);
        }
        var automatic = group.Alternatives
            .OrderByDescending(entry => GetEvaluation(entry).IsSupported)
            .ThenByDescending(entry => GetEvaluation(entry).Relevance)
            .ThenBy(entry => GetEvaluation(entry).MissingUnits)
            .ThenBy(entry => entry.StableId, StringComparer.Ordinal)
            .First();
        return new(group, automatic, GetEvaluation(automatic), false);
    }

    internal ImmutableArray<CraftingRecipeSelection> Search(string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.ToUpperInvariant()).ToArray();
        return catalogue.Groups.Select(group => GetSelection(group.Id))
            .Where(selection => words.All(word => selection.Recipe.SearchText.Contains(word, StringComparison.Ordinal)))
            .OrderByDescending(selection => selection.Evaluation.IsSupported)
            .ThenByDescending(selection => selection.Evaluation.CanCraft)
            .ThenByDescending(selection => selection.Evaluation.Relevance)
            .ThenBy(selection => selection.Evaluation.MissingUnits)
            .ThenBy(selection => selection.Recipe.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(selection => selection.Recipe.StableId, StringComparer.Ordinal)
            .ToImmutableArray();
    }

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
