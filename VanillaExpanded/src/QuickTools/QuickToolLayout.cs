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

    /// <summary>Creates geometry for the currently available identifiers in canonical order.</summary>
    public static RadialMenuLayout CreateLayout(IReadOnlyList<string> availableIds)
    {
        ArgumentNullException.ThrowIfNull(availableIds);
        var supported = new HashSet<string>(Ids, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in availableIds)
        {
            if (!supported.Contains(id) || !seen.Add(id)) throw new ArgumentException("Expected distinct supported identifiers.", nameof(availableIds));
        }
        return new RadialMenuLayout(availableIds, RestoreId, 0.18, 0.24, 1, 0, true);
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
        for (int i = 0; i < Categories.Length; i++) ids[i] = $"tool:{Categories[i]}";
        ids[^1] = LightId;
        return ids;
    }
    #endregion
}
