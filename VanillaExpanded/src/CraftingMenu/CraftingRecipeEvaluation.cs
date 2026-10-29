using System;
using System.Collections.Immutable;

namespace VanillaExpanded.CraftingMenu;

internal sealed record CraftingMissingRequirement(string IngredientId, int Quantity);

internal sealed record CraftingRecipeEvaluation
{
    private CraftingRecipeEvaluation(bool supported, string? reason, int? allocated, int? required,
        bool canCraft, ImmutableArray<CraftingMissingRequirement> missing)
    {
        IsSupported = supported;
        UnsupportedReason = reason;
        Allocated = allocated;
        Required = required;
        CanCraft = canCraft;
        Missing = missing;
    }

    internal bool IsSupported { get; }
    internal string? UnsupportedReason { get; }
    internal int? Allocated { get; }
    internal int? Required { get; }
    internal double? Relevance => Required is int required
        ? required == 0 ? 1 : (double)Allocated!.Value / required : null;
    internal int? MissingUnits => Required - Allocated;
    internal bool CanCraft { get; }
    internal ImmutableArray<CraftingMissingRequirement> Missing { get; }

    internal static CraftingRecipeEvaluation Unsupported(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(false, reason, null, null, false, ImmutableArray<CraftingMissingRequirement>.Empty);
    }

    internal static CraftingRecipeEvaluation Supported(int allocated, int required, bool canCraft,
        ImmutableArray<CraftingMissingRequirement> missing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(required);
        if (allocated < 0 || allocated > required) throw new ArgumentOutOfRangeException(nameof(allocated));
        if (canCraft && allocated != required)
            throw new ArgumentException("Craftability requires complete allocation.", nameof(canCraft));
        if (missing.IsDefault) throw new ArgumentException("Missing requirements must be initialized.", nameof(missing));
        if (canCraft && missing.Length > 0)
            throw new ArgumentException("Craftable recipes cannot have missing requirements.", nameof(missing));
        foreach (var requirement in missing)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(requirement.IngredientId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requirement.Quantity);
        }
        return new(true, null, allocated, required, canCraft, missing);
    }
}
