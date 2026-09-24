using System;
using System.Collections.Generic;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Owns the inventory-independent ordering of supported quick-tool identifiers.</summary>
public static class QuickToolLayout
{
    /// <summary>Gets the stable identifier for the light selection provider.</summary>
    public const string LightId = "virtual:light-source";

    /// <summary>Gets the stable center restoration identifier.</summary>
    public const string RestoreId = "unequip";

    private static readonly EnumTool[] Categories =
    [
        EnumTool.Knife, EnumTool.Pickaxe, EnumTool.Axe, EnumTool.Sword, EnumTool.Shovel,
        EnumTool.Hammer, EnumTool.Spear, EnumTool.Bow, EnumTool.Shears, EnumTool.Sickle,
        EnumTool.Hoe, EnumTool.Saw, EnumTool.Chisel, EnumTool.Scythe, EnumTool.Sling,
        EnumTool.Wrench, EnumTool.Probe, EnumTool.Meter, EnumTool.Drill, EnumTool.Firearm,
        EnumTool.Crossbow, EnumTool.Javelin, EnumTool.Pike, EnumTool.Shield, EnumTool.Club,
        EnumTool.Mace, EnumTool.Warhammer, EnumTool.Poleaxe, EnumTool.Halberd, EnumTool.Polearm,
        EnumTool.Staff, EnumTool.Tongs, EnumTool.Crowbar
    ];

    private static readonly IReadOnlyList<string> Ids = Array.AsReadOnly(BuildIds());
    private static readonly HashSet<EnumTool> Supported = [.. Categories];

    #region Public API
    /// <summary>Gets supported identifiers in their clockwise menu order.</summary>
    public static IReadOnlyList<string> WedgeIds => Ids;

    /// <summary>Builds a stable entry identifier directly from a discovered tool category tag.</summary>
    public static string GetToolId(string toolTag) => "tool:" + toolTag[5..];

    /// <summary>Checks whether a tag names one concrete tool category rather than the generic tool tag.</summary>
    public static bool TryGetToolTag(string toolTag, out string normalizedTag)
    {
        normalizedTag = string.Empty;
        if (string.IsNullOrWhiteSpace(toolTag) || !toolTag.StartsWith("tool-", StringComparison.Ordinal) || toolTag.Length == 5) return false;
        normalizedTag = toolTag.ToLowerInvariant();
        return true;
    }

    /// <summary>Resolves a dynamic entry identifier back to its originating category tag.</summary>
    public static bool TryGetToolTagFromId(string id, out string toolTag)
        => TryGetToolTag(id is not null && id.StartsWith("tool:", StringComparison.Ordinal) ? "tool-" + id[5..] : string.Empty, out toolTag);

    /// <summary>Creates geometry for the currently available identifiers in canonical order.</summary>
    public static RadialMenuLayout CreateLayout(IReadOnlyList<string> availableIds)
    {
        ArgumentNullException.ThrowIfNull(availableIds);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in availableIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw new ArgumentException("Expected distinct nonempty identifiers.", nameof(availableIds));
        }
        return new RadialMenuLayout(availableIds, RestoreId, 0.24, 0.30, 1, 0, true, separatorDegrees: 1.5);
    }

    /// <summary>Returns an identifier only for a category in the explicit supported set.</summary>
    public static string? GetToolId(EnumTool category) => Supported.Contains(category) ? $"tool:{category}" : null;

    /// <summary>Checks that a stable identifier belongs to the explicit tool set.</summary>
    public static bool TryGetTool(string id, out EnumTool category)
    {
        category = default;
        return id is not null && id.StartsWith("tool:", StringComparison.Ordinal)
            && Enum.TryParse(id.AsSpan(5), false, out category) && Supported.Contains(category);
    }
    #endregion

    #region Construction
    /// <summary>Builds identifiers from the explicit table, independent of enum or inventory enumeration.</summary>
    private static string[] BuildIds()
    {
        var ids = new string[Categories.Length + 1];
        ids[0] = LightId;
        for (int i = 0; i < Categories.Length; i++) ids[i + 1] = $"tool:{Categories[i]}";
        return ids;
    }
    #endregion
}
