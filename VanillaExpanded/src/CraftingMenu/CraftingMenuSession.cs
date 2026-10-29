using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingMenuSession
{
    private readonly CraftingCatalogueView view;
    private readonly CraftingAllocator allocator;
    private readonly System.Func<CraftingInventorySnapshot> capture;
    private readonly System.Func<CraftingRecipeEntry, bool, CraftingDispatchResult> execute;
    private readonly CraftingCatalogue catalogue;
    private ImmutableDictionary<int, CraftingAllocation> allocations = ImmutableDictionary<int, CraftingAllocation>.Empty;
    private int? interaction;
    private string query = "";

    internal CraftingMenuSession(CraftingCatalogue catalogue, CraftingAllocator allocator,
        System.Func<CraftingInventorySnapshot> capture,
        System.Func<CraftingRecipeEntry, bool, CraftingDispatchResult> execute)
    {
        this.catalogue = catalogue;
        view = new(catalogue);
        this.allocator = allocator;
        this.capture = capture;
        this.execute = execute;
    }

    internal ImmutableArray<CraftingRecipeSelection> Rows { get; private set; } = ImmutableArray<CraftingRecipeSelection>.Empty;
    internal int? SelectedOutput { get; private set; }
    internal event Action? Changed;

    internal void Refresh()
    {
        allocations = allocator.EvaluateCatalogue(catalogue, capture());
        view.ReplaceAllocations(allocations);
        UpdateRows();
    }

    internal void Search(string text)
    {
        query = text;
        interaction = null;
        UpdateRows();
    }

    internal void Interact(int? output)
    {
        if (interaction == output) return;
        interaction = output;
        if (output == null) UpdateRows();
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
        if (SelectedOutput is not int output) return;
        view.SelectAlternative(output, recipe);
        UpdateRows(output);
    }

    internal void Auto()
    {
        if (SelectedOutput is not int output) return;
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
        if (interaction != null && !Rows.IsEmpty)
        {
            Rows = Rows.Select(row => changedOutput == row.Output.Id ? view.GetSelection(row.Output.Id)
                : row with { Evaluation = allocations[row.Recipe.Id].Evaluation, Allocation = allocations[row.Recipe.Id] })
                .ToImmutableArray();
        }
        else Rows = view.Search(query);
        Changed?.Invoke();
    }
}
