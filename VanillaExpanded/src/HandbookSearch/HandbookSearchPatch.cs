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
    private const float FullWordBonus = 0.5f;

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

        if (WordBoundaryMatcher.ContainsFullWord(MemoryExtensions.AsSpan(title), MemoryExtensions.AsSpan(searchText)))
        {
            __result += FullWordBonus;
        }
    }
}
