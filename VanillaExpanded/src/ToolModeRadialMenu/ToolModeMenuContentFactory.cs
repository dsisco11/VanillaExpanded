using System;
using System.Collections.Generic;
using System.Globalization;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Converts vanilla skill items into the bounded generic radial-menu contract.</summary>
internal static class ToolModeMenuContentFactory
{
    private const int MaximumModes = 63;
    private const string CenterId = "current";

    /// <summary>Builds radial content, or declines mode sets that must remain on the vanilla grid.</summary>
    internal static bool TryCreate(SkillItem[]? modes, int currentMode, string fallbackCurrentLabel, out ToolModeMenuContent? content)
        => TryCreate(modes, currentMode, fallbackCurrentLabel, smithingHammer: false, out content);

    /// <summary>Builds the smithing hammer's padded ring with directional modes fixed to cardinal wedges.</summary>
    internal static bool TryCreateSmithingHammer(SkillItem[]? modes, int currentMode, string fallbackCurrentLabel,
        out ToolModeMenuContent? content)
        => TryCreate(modes, currentMode, fallbackCurrentLabel, smithingHammer: true, out content);

    private static bool TryCreate(SkillItem[]? modes, int currentMode, string fallbackCurrentLabel, bool smithingHammer,
        out ToolModeMenuContent? content)
    {
        content = null;
        if (modes is null || modes.Length is 0 or > MaximumModes
            || Array.Exists(modes, static mode => mode is null || mode.Name is null)) return false;

        var ids = new string[modes.Length];
        var entries = new List<RadialMenuEntry>(modes.Length + 1);
        for (int index = 0; index < modes.Length; index++)
        {
            SkillItem mode = modes[index];
            string id = index.ToString(CultureInfo.InvariantCulture);
            ids[index] = id;
            IRadialMenuIcon? icon = mode.Texture is null && mode.RenderHandler is null ? null : new ToolModeSkillIcon(mode);
            entries.Add(new RadialMenuEntry(id, mode.Name, mode.Enabled, icon, mode.Description));
        }

        string currentLabel = currentMode >= 0 && currentMode < modes.Length
            ? modes[currentMode].Name
            : fallbackCurrentLabel;
        entries.Add(new RadialMenuEntry(CenterId, currentLabel, enabled: false));
        var currentMenu = new RadialMenuLayout([CenterId], 0, 0.25);
        bool useSmithingLayout = smithingHammer && CanCreateSmithingLayout(modes);
        RadialMenuLayout layout = useSmithingLayout
            ? CreateSmithingLayout(modes, ids, entries, currentMenu)
            : CreateGenericLayout(ids, currentMenu);
        content = new ToolModeMenuContent(
            layout,
            entries);
        return true;
    }

    private static bool CanCreateSmithingLayout(SkillItem[] modes)
    {
        bool hasDirectional = false;
        bool hasAction = false;
        var directions = new HashSet<int>();
        foreach (SkillItem mode in modes)
        {
            int direction = GetUpsetDirection(mode.Code?.Path);
            if (direction >= 0)
            {
                if (!directions.Add(direction)) return false;
                hasDirectional = true;
            }
            else hasAction = true;
        }
        int wedgeCount = (modes.Length + 3) / 4 * 4;
        return hasDirectional && hasAction && wedgeCount <= 63;
    }

    private static RadialMenuLayout CreateSmithingLayout(SkillItem[] modes, string[] ids,
        List<RadialMenuEntry> entries, RadialMenuLayout currentMenu)
    {
        int wedgeCount = (modes.Length + 3) / 4 * 4;
        var slots = new string?[wedgeCount];
        var actions = new List<string>();
        for (int index = 0; index < modes.Length; index++)
        {
            int direction = GetUpsetDirection(modes[index].Code?.Path);
            if (direction >= 0) slots[direction * wedgeCount / 4] = ids[index];
            else actions.Add(ids[index]);
        }

        int actionIndex = 0;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot] is not null) continue;
            if (actionIndex < actions.Count)
            {
                slots[slot] = actions[actionIndex++];
                continue;
            }
            string paddingId = $"padding:{slot}";
            slots[slot] = paddingId;
            entries.Add(new RadialMenuEntry(paddingId, string.Empty, enabled: false));
        }

        return new RadialMenuLayout(Array.ConvertAll(slots, static id => id!), 0.30, 1, currentMenu,
            separatorDegrees: 1.5, radiusScale: 0.6);
    }

    private static RadialMenuLayout CreateGenericLayout(string[] ids, RadialMenuLayout currentMenu) =>
        new(ids, 0.30, 1, currentMenu, separatorDegrees: 1.5, radiusScale: 0.6);

    private static int GetUpsetDirection(string? code) => code switch
    {
        "upsetup" => 0,
        "upsetright" => 1,
        "upsetdown" => 2,
        "upsetleft" => 3,
        _ => -1
    };

    /// <summary>Parses only the invariant nonnegative identifiers emitted by <see cref="TryCreate"/>.</summary>
    internal static bool TryGetMode(string id, out int mode) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out mode);
}