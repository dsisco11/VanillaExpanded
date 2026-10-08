using System;
using System.Collections.Generic;
using System.Globalization;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Config;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Builds bounded pages of material stacks with navigation independent of numeric tool-mode IDs.</summary>
internal static class ChiselMaterialMenuContentFactory
{
    internal const int PageSize = 12;
    internal const string BackId = "chisel-material:back";
    internal const string PreviousId = "chisel-material:previous";
    internal const string NextId = "chisel-material:next";
    private const string CandidatePrefix = "chisel-material:stack:";

    #region Public API
    /// <summary>Creates one page, retaining a separate Back disc even when only one stack is available.</summary>
    internal static ToolModeMenuContent Create(IReadOnlyList<ChiselMaterialCandidate> candidates, int page = 0)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int lastPage = candidates.Count == 0 ? 0 : (candidates.Count - 1) / PageSize;
        page = Math.Clamp(page, 0, lastPage);
        var entries = new List<RadialMenuEntry>();
        var ids = new List<string>();
        int start = page * PageSize;
        int end = (int)Math.Min((long)start + PageSize, candidates.Count);
        for (int index = start; index < end; index++)
        {
            var stack = candidates[index].Stack;
            string id = CandidatePrefix + index.ToString(CultureInfo.InvariantCulture);
            ids.Add(id);
            entries.Add(new RadialMenuEntry(id, stack.GetName() + " ×" + stack.StackSize.ToString(CultureInfo.CurrentCulture),
                true, new ChiselMaterialItemIcon(stack)));
        }
        // An explanatory disabled wedge leaves cancellation/navigation available in the empty state.
        if (candidates.Count == 0)
        {
            ids.Add("chisel-material:empty");
            entries.Add(new RadialMenuEntry(ids[0], Lang.Get("vanillaexpanded:chisel-material-empty"), false));
        }
        if (page > 0)
        {
            ids.Add(PreviousId);
            entries.Add(new RadialMenuEntry(PreviousId, Lang.Get("vanillaexpanded:chisel-material-previous"), true));
        }
        if (page < lastPage)
        {
            ids.Add(NextId);
            entries.Add(new RadialMenuEntry(NextId, Lang.Get("vanillaexpanded:chisel-material-next"), true));
        }
        entries.Add(new RadialMenuEntry(BackId, Lang.Get("vanillaexpanded:chisel-material-back"), true));
        var back = new RadialMenuLayout([BackId], 0, ToolModeMenuGeometry.GetCenterRadius(ToolModeMenuGeometry.MaterialCenterRadius));
        var layout = new RadialMenuLayout(ids, back.OuterRadius + RadialMenuWedgeStyle.RingGapFraction,
            ToolModeMenuGeometry.GetOuterRadius(back.OuterRadius + RadialMenuWedgeStyle.RingGapFraction), back, separatorDegrees: ToolModeMenuGeometry.SeparatorDegrees, radiusScale: ToolModeMenuGeometry.DefaultRadiusScale,
            sizeMultiplier: static () => VanillaExpandedModSystem.Config.ToolModeMenuSize);
        return new ToolModeMenuContent(layout, entries);
    }

    /// <summary>Recognizes only prefixed nonnegative stack indexes, leaving vanilla mode IDs untouched.</summary>
    internal static bool TryGetCandidateIndex(string id, out int index)
    {
        index = 0;
        return id.StartsWith(CandidatePrefix, StringComparison.Ordinal)
            && int.TryParse(id.AsSpan(CandidatePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
    #endregion
}
