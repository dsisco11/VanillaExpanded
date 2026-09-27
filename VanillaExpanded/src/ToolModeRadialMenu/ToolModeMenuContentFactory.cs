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

    /// <summary>Builds the smithing hammer's directional outer ring and action inner ring.</summary>
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
            ? CreateSmithingLayout(modes, ids)
            : CreateGenericLayout(ids, currentMenu);
        if (useSmithingLayout) entries.RemoveAt(entries.Count - 1);
        content = new ToolModeMenuContent(
            layout,
            entries);
        return true;
    }

    private static bool CanCreateSmithingLayout(SkillItem[] modes)
    {
        bool hasDirectional = false;
        bool hasAction = false;
        foreach (SkillItem mode in modes)
        {
            if (GetUpsetDirection(mode.Code?.Path) >= 0) hasDirectional = true;
            else hasAction = true;
        }
        return hasDirectional && hasAction;
    }

    private static RadialMenuLayout CreateSmithingLayout(SkillItem[] modes, string[] ids)
    {
        var directional = new List<(int Index, int Direction)>();
        var actions = new List<string>();
        for (int index = 0; index < modes.Length; index++)
        {
            int direction = GetUpsetDirection(modes[index].Code?.Path);
            if (direction >= 0) directional.Add((index, direction));
            else actions.Add(ids[index]);
        }

        directional.Sort(static (left, right) =>
        {
            int direction = left.Direction.CompareTo(right.Direction);
            return direction != 0 ? direction : left.Index.CompareTo(right.Index);
        });
        string[] directionalIds = [.. directional.ConvertAll(item => ids[item.Index])];
        var actionMenu = new RadialMenuLayout(actions, 0, 0.25, renderAsCenter: true);
        return new RadialMenuLayout(directionalIds, 0.30, 1, actionMenu,
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