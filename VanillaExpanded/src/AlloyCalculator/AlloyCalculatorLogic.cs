using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>
/// Static helper methods for alloy calculator logic.
/// Extracted for testability - all methods are pure functions with no external dependencies.
/// </summary>
internal static class AlloyCalculatorLogic
{
    #region Slot Allocation

    /// <summary>
    /// Allocates cooking slots proportionally based on ingredient amounts.
    /// Larger amounts get more slots, with at least 1 slot per ingredient.
    /// </summary>
    /// <param name="ingredientAmounts">List of target amounts for each ingredient.</param>
    /// <param name="totalSlots">Total number of available slots.</param>
    /// <returns>Array of slot counts allocated to each ingredient.</returns>
    public static int[] AllocateSlotsProportionally(IReadOnlyList<int> ingredientAmounts, int totalSlots)
    {
        var allocations = new int[ingredientAmounts.Count];

        if (ingredientAmounts.Count == 0 || totalSlots <= 0) return allocations;

        // If more ingredients than slots, give 1 slot each until we run out
        if (ingredientAmounts.Count >= totalSlots)
        {
            for (var i = 0; i < Math.Min(ingredientAmounts.Count, totalSlots); i++)
            {
                allocations[i] = 1;
            }
            return allocations;
        }

        // First: guarantee each ingredient gets at least 1 slot
        for (var i = 0; i < ingredientAmounts.Count; i++)
        {
            allocations[i] = 1;
        }

        var remainingSlots = totalSlots - ingredientAmounts.Count;
        if (remainingSlots <= 0) return allocations;

        // Second: distribute remaining slots proportionally to larger amounts
        var totalItems = ingredientAmounts.Sum();

        if (totalItems > 0)
        {
            for (var i = 0; i < ingredientAmounts.Count && remainingSlots > 0; i++)
            {
                var proportion = (double)ingredientAmounts[i] / totalItems;
                var extraSlots = (int)Math.Round(proportion * remainingSlots);
                allocations[i] += extraSlots;
            }
        }

        // Ensure we don't exceed total slots
        var totalAllocated = allocations.Sum();
        while (totalAllocated > totalSlots)
        {
            for (var i = ingredientAmounts.Count - 1; i >= 0 && totalAllocated > totalSlots; i--)
            {
                if (allocations[i] > 1)
                {
                    allocations[i]--;
                    totalAllocated--;
                }
            }
        }

        // Distribute any unused slots to largest ingredients
        while (totalAllocated < totalSlots)
        {
            for (var i = 0; i < ingredientAmounts.Count && totalAllocated < totalSlots; i++)
            {
                allocations[i]++;
                totalAllocated++;
            }
        }

        return allocations;
    }

    #endregion

    #region Percentage Normalization

    /// <summary>
    /// Normalizes slider values to ensure they sum to 100%, respecting min/max constraints.
    /// </summary>
    /// <param name="sliderValues">Current slider values by index.</param>
    /// <param name="constraints">Min and max percentages for each slider index.</param>
    /// <param name="changedIndex">Index of the slider that was just changed (-1 for initial normalization).</param>
    /// <returns>Normalized slider values dictionary.</returns>
    public static Dictionary<int, int> NormalizePercentages(
        IReadOnlyDictionary<int, int> sliderValues,
        IReadOnlyDictionary<int, (int min, int max)> constraints,
        int changedIndex)
    {
        var result = new Dictionary<int, int>(sliderValues);

        var totalPercent = result.Values.Sum();
        var difference = totalPercent - 100;

        if (Math.Abs(difference) < 1) return result; // Already at 100%

        // Distribute the difference among other sliders proportionally
        var otherIndices = result.Keys.Where(i => i != changedIndex).ToList();
        if (otherIndices.Count == 0) return result;

        // Calculate how much each other slider can absorb
        var adjustments = new Dictionary<int, int>();
        var totalAdjustable = 0.0;

        foreach (var idx in otherIndices)
        {
            if (!constraints.TryGetValue(idx, out var constraint)) continue;

            var currentValue = result[idx];

            // If we need to decrease (difference > 0), check how much we can decrease
            // If we need to increase (difference < 0), check how much we can increase
            var adjustable = difference > 0
                ? currentValue - constraint.min
                : constraint.max - currentValue;

            adjustments[idx] = Math.Max(0, adjustable);
            totalAdjustable += Math.Max(0, adjustable);
        }

        if (totalAdjustable <= 0) return result;

        // Apply proportional adjustments
        var remaining = Math.Abs(difference);
        foreach (var idx in otherIndices)
        {
            if (remaining <= 0) break;
            if (!adjustments.TryGetValue(idx, out var adjustable) || adjustable <= 0) continue;

            var proportion = adjustable / totalAdjustable;
            var adjustment = (int)Math.Round(Math.Abs(difference) * proportion);
            adjustment = Math.Min(adjustment, adjustable);
            adjustment = Math.Min(adjustment, remaining);

            if (difference > 0)
            {
                result[idx] -= adjustment;
            }
            else
            {
                result[idx] += adjustment;
            }

            remaining -= adjustment;
        }

        return result;
    }

    /// <summary>
    /// Calculates the midpoint percentage for an ingredient's valid range.
    /// </summary>
    /// <param name="minRatio">Minimum ratio (0.0 - 1.0).</param>
    /// <param name="maxRatio">Maximum ratio (0.0 - 1.0).</param>
    /// <returns>Midpoint percentage (0-100).</returns>
    public static int CalculateMidpointPercentage(double minRatio, double maxRatio)
    {
        var minPercent = (int)Math.Round(minRatio * 100);
        var maxPercent = (int)Math.Round(maxRatio * 100);
        return (minPercent + maxPercent) / 2;
    }

    #endregion

    #region Nugget Calculation

    /// <summary>
    /// Calculates the number of nuggets required for a given percentage of target units.
    /// Uses 5 units per nugget and rounds up.
    /// </summary>
    /// <param name="targetUnits">Total units of alloy to create.</param>
    /// <param name="percentage">Percentage of this ingredient (0-100).</param>
    /// <returns>Number of nuggets required.</returns>
    public static int CalculateNuggetsRequired(int targetUnits, int percentage)
    {
        if (targetUnits <= 0 || percentage <= 0) return 0;

        var units = targetUnits * percentage / 100.0;
        return (int)Math.Ceiling(units / 5.0); // 1 nugget = 5 units, round up
    }

    /// <summary>
    /// Calculates nuggets required for multiple ingredients.
    /// </summary>
    /// <param name="targetUnits">Total units of alloy to create.</param>
    /// <param name="percentages">Percentages for each ingredient by index.</param>
    /// <returns>Dictionary of nugget counts by ingredient index.</returns>
    public static Dictionary<int, int> CalculateAllNuggetsRequired(int targetUnits, IReadOnlyDictionary<int, int> percentages)
    {
        var result = new Dictionary<int, int>();

        foreach (var (index, percentage) in percentages)
        {
            var nuggets = CalculateNuggetsRequired(targetUnits, percentage);
            if (nuggets > 0)
            {
                result[index] = nuggets;
            }
        }

        return result;
    }

    #endregion

    #region Display Names

    /// <summary>
    /// Gets the display name for a material based on its asset location.
    /// Extracts the end variant and looks up the localized material name.
    /// </summary>
    /// <param name="assetLocation">The asset location to get the display name for.</param>
    /// <returns>Localized material display name, or the asset path if not found.</returns>
    public static string GetMaterialDisplayName(AssetLocation? assetLocation)
    {
        if (assetLocation is null) return "unknown";

        var materialCode = assetLocation.EndVariant();
        return Lang.GetMatching($"material-{materialCode}") ?? assetLocation.Path;
    }

    /// <summary>
    /// Gets the display name for an alloy recipe.
    /// </summary>
    /// <param name="outputCode">The alloy output asset location.</param>
    /// <returns>Localized alloy display name.</returns>
    public static string GetAlloyDisplayName(AssetLocation? outputCode)
    {
        return GetMaterialDisplayName(outputCode);
    }

    /// <summary>
    /// Gets the display name for an alloy ingredient.
    /// </summary>
    /// <param name="ingredientCode">The ingredient asset location.</param>
    /// <returns>Localized ingredient display name.</returns>
    public static string GetIngredientDisplayName(AssetLocation? ingredientCode)
    {
        return GetMaterialDisplayName(ingredientCode);
    }

    /// <summary>
    /// Extracts the material code from an asset location path.
    /// E.g., "metalbit-copper" -> "copper", "game:ingot-iron" -> "iron"
    /// </summary>
    /// <param name="path">The asset path.</param>
    /// <returns>The material code.</returns>
    public static string ExtractMaterialCode(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "unknown";

        // Remove domain prefix if present (e.g., "game:")
        var colonIndex = path.IndexOf(':');
        if (colonIndex >= 0)
        {
            path = path[(colonIndex + 1)..];
        }

        // Extract last segment after dash (only if there's content after)
        var lastDash = path.LastIndexOf('-');
        if (lastDash >= 0 && lastDash < path.Length - 1)
        {
            return path[(lastDash + 1)..];
        }

        return path;
    }

    #endregion
}
