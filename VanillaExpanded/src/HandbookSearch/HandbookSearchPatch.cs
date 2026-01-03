using System;

using HarmonyLib;

using Vintagestory.GameContent;

namespace VanillaExpanded.HandbookSearch;

/// <summary>
/// Harmony patch to prioritize full-word matches in handbook search results.
/// Boosts the weight of results where the search term appears as a complete word.
/// </summary>
[Harmony]
internal static class HandbookSearchPatch
{
    /// <summary>
    /// Weight bonus applied when search text matches a complete word in the title.
    /// Applied when the original weight is in the "contains" range (below 2.5).
    /// </summary>
    private const float FullWordBonus = 0.4f;

    /// <summary>
    /// Additional bonus for matches appearing early in the title.
    /// Reduced by PositionPenaltyPerWord for each word position.
    /// </summary>
    private const float MaxPositionBonus = 0.1f;

    /// <summary>
    /// Penalty per word position from the start of the title.
    /// First word (position 0) gets full bonus, each subsequent word loses this amount.
    /// </summary>
    private const float PositionPenaltyPerWord = 0.02f;

    /// <summary>
    /// Threshold below which we consider the match to be a "contains" match
    /// (not exact or starts-with) and apply the full-word bonus.
    /// </summary>
    private const float ContainsMatchThreshold = 2.0f;

    /// <summary>
    /// Minimum weight required for us to consider boosting (must have matched something).
    /// </summary>
    private const float MinimumMatchWeight = 0.01f;

    [HarmonyPatch(typeof(GuiHandbookItemStackPage), nameof(GuiHandbookItemStackPage.GetTextMatchWeight))]
    [HarmonyPostfix]
    private static void GetTextMatchWeight_Postfix(
        GuiHandbookItemStackPage __instance,
        string searchText,
        ref float __result)
    {
        // Early exit if feature is disabled
        if (!VanillaExpandedModSystem.Config.EnableHandbookSearchPrioritization)
            return;

        // Only boost if we have a "contains" match (not exact or starts-with)
        // Original weight scale:
        //   3.0  = exact match
        //   2.75 = starts with search + space
        //   2.5  = starts with search
        //   2.0  = title contains search
        //   1.0  = description contains search
        if (__result < MinimumMatchWeight || __result >= ContainsMatchThreshold)
            return;

        // Check if the search text appears as a complete word in the title
        string title = __instance.TextCacheTitle;
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(searchText))
            return;

        if (WordBoundaryMatcher.TryGetFullWordPosition(
            MemoryExtensions.AsSpan(title),
            MemoryExtensions.AsSpan(searchText),
            out int wordPosition))
        {
            // Base bonus for full-word match
            float bonus = FullWordBonus;

            // Position bonus: earlier words get more boost (clamped to 0 minimum)
            float positionBonus = Math.Max(0f, MaxPositionBonus - (wordPosition * PositionPenaltyPerWord));
            bonus += positionBonus;

            __result += bonus;
        }
    }
}
