using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaExpanded.CraftingMenu;

internal enum CraftingMenuEmptyState { None, NoRecipes, NoSearchMatches, NoCraftableRecipes, NoCraftableSearchMatches }

internal sealed class CraftingMenuSession : IDisposable
{
    private readonly CraftingCatalogueView view;
    private readonly CraftingAllocator allocator;
    private readonly System.Func<CraftingInventorySnapshot> capture;
    private readonly System.Func<CraftingRecipeEntry, bool, CraftingDispatchResult> execute;
    private readonly CraftingCatalogue catalogue;
    private readonly RecipeSearch search;
    private readonly RecipeCategory category;
    private int? interaction;

    internal CraftingMenuSession(CraftingCatalogue catalogue, CraftingAllocator allocator,
        System.Func<CraftingInventorySnapshot> capture,
        System.Func<CraftingRecipeEntry, bool, CraftingDispatchResult> execute)
    {
        this.catalogue = catalogue;
        view = new(catalogue);
        this.allocator = allocator;
        this.capture = capture;
        this.execute = execute;
        search = new(Filters, ProjectionChanged);
        category = new(catalogue, Filters, CategoryChanged);
        CraftableOnly = new(Filters, ProjectionChanged);
        Sort = new(ProjectionChanged);
    }

    internal ImmutableArray<CraftingRecipeSelection> Rows { get; private set; } = ImmutableArray<CraftingRecipeSelection>.Empty;
    internal int? SelectedOutput { get; private set; }
    internal string? Category => category.Selected;
    internal RecipeFilterSet Filters { get; } = new();
    internal CraftableOnly CraftableOnly { get; }
    internal RecipeSort Sort { get; }
    internal CraftingMenuEmptyState EmptyState { get; private set; }
    internal event Action? Changed;

    internal void Refresh()
    {
        view.ReplaceAllocations(allocator.EvaluateCatalogue(catalogue, capture()));
        UpdateRows();
    }

    internal void Search(string text) => search.SetText(text);

    private void ProjectionChanged()
    {
        interaction = null;
        UpdateRows();
    }

    internal void Interact(int? output)
    {
        if (interaction == output) return;
        interaction = output;
        if (output == null) UpdateRows();
    }

    internal void SelectCategory(string? selected) => category.Select(selected);

    private void CategoryChanged()
    {
        if (SelectedOutput is int output) view.Deselect(output);
        SelectedOutput = null;
        ProjectionChanged();
    }

    internal void Select(int output)
    {
        int? previous = SelectedOutput;
        if (previous != output && previous is int id) view.Deselect(id);
        SelectedOutput = output;
        if (previous != output) UpdateRows(previous);
    }

    internal void ChooseAlternative(int recipe)
    {
        if (SelectedOutput is not int output || !Rows.Any(row => row.Output.Id == output)) return;
        view.SelectAlternative(output, recipe);
        UpdateRows(output);
    }

    internal void Auto()
    {
        if (SelectedOutput is not int output || !Rows.Any(row => row.Output.Id == output)) return;
        view.SelectAuto(output);
        UpdateRows(output);
    }

    internal CraftingDispatchResult Activate(int output, bool shift)
    {
        var shown = Rows.First(row => row.Output.Id == output);
        Select(output);
        if (!shown.CanPlace)
            return new(CraftingDispatchStatus.Rejected, shown.Evaluation.UnsupportedReason, 0);
        // The coordinator captures and allocates again; displayed order is not authority.
        return execute(shown.Recipe, shift);
    }

    private void UpdateRows(int? changedOutput = null)
    {
        var canonical = view.GetRows(Filters, Sort.Selected);
        var replacement = canonical;
        if (interaction != null && !Rows.IsEmpty)
        {
            var eligible = Filters.Filter(Rows.Select(row => changedOutput == row.Output.Id
                ? view.GetSelection(row.Output.Id) : view.RefreshSelection(row)))
                .ToDictionary(row => row.Output.Id);
            var candidates = canonical.ToDictionary(row => row.Output.Id);
            var emitted = new HashSet<int>();
            var stable = ImmutableArray.CreateBuilder<CraftingRecipeSelection>();
            foreach (var row in Rows)
            {
                if (eligible.TryGetValue(row.Output.Id, out var current)
                    || candidates.TryGetValue(row.Output.Id, out current))
                {
                    stable.Add(current);
                    emitted.Add(current.Output.Id);
                }
            }
            foreach (var row in canonical)
                if (emitted.Add(row.Output.Id)) stable.Add(row);
            replacement = stable.ToImmutable();
        }
        var emptyState = DiagnoseEmptyState(replacement);
        Rows = replacement;
        EmptyState = emptyState;
        Changed?.Invoke();
    }

    private CraftingMenuEmptyState DiagnoseEmptyState(ImmutableArray<CraftingRecipeSelection> rows)
    {
        if (!rows.IsEmpty) return CraftingMenuEmptyState.None;
        if (catalogue.Groups.IsEmpty) return CraftingMenuEmptyState.NoRecipes;
        if (CraftableOnly.Enabled
            && !CraftableOnly.FilterOthers(catalogue.Groups.Select(group => view.GetSelection(group.Id))).IsEmpty)
            return search.HasTerms ? CraftingMenuEmptyState.NoCraftableSearchMatches : CraftingMenuEmptyState.NoCraftableRecipes;
        return search.HasTerms ? CraftingMenuEmptyState.NoSearchMatches : CraftingMenuEmptyState.NoRecipes;
    }

    public void Dispose()
    {
        Filters.Dispose();
        Changed = null;
    }
}
