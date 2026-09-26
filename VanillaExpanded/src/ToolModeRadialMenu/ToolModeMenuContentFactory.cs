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
        content = new ToolModeMenuContent(
            new RadialMenuLayout(ids, CenterId, 0.24, 0.30, 1, separatorDegrees: 1.5),
            entries);
        return true;
    }

    /// <summary>Parses only the invariant nonnegative identifiers emitted by <see cref="TryCreate"/>.</summary>
    internal static bool TryGetMode(string id, out int mode) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out mode);
}