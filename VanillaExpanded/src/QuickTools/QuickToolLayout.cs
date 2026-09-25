using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Builds a stable entry identifier from a complete normalized set of discovered tool and weapon category tags.</summary>
    public static string GetToolId(IReadOnlyCollection<string> toolTags)
    {
        ArgumentNullException.ThrowIfNull(toolTags);
        string[] normalized = toolTags.Select(tag => TryGetToolTag(tag, out string value) ? value : throw new ArgumentOutOfRangeException(nameof(toolTags)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 0) throw new ArgumentException("Expected at least one concrete tool tag.", nameof(toolTags));
        return "tool:" + string.Join('+', normalized.Select(tag => tag.StartsWith("tool-", StringComparison.Ordinal) ? tag[5..] : tag));
    }

    /// <summary>Checks whether a tag names one concrete tool or weapon category rather than a generic tag.</summary>
    public static bool TryGetToolTag(string toolTag, out string normalizedTag)
    {
        normalizedTag = string.Empty;
        if (string.IsNullOrWhiteSpace(toolTag)
            || (!toolTag.StartsWith("tool-", StringComparison.Ordinal) && !toolTag.StartsWith("weapon-", StringComparison.Ordinal))
            || toolTag is "tool-" or "weapon-") return false;
        normalizedTag = toolTag.ToLowerInvariant();
        return true;
    }

    /// <summary>Resolves a dynamic entry identifier back to its complete tool and weapon category tag set.</summary>
    public static bool TryGetToolTagsFromId(string id, out string[] toolTags)
    {
        toolTags = [];
        if (id is null || !id.StartsWith("tool:", StringComparison.Ordinal)) return false;
        string[] values = id[5..].Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 0) return false;
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
            if (!TryGetToolTag(value.StartsWith("weapon-", StringComparison.Ordinal) ? value : "tool-" + value,
                out string toolTag) || !normalized.Add(toolTag)) return false;
        toolTags = [.. normalized];
        return true;
    }

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
