using System;
using System.Collections.Generic;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Places hammer upset modes at cardinal positions in a padded ring.</summary>
internal sealed class SmithingHammerToolModeMenuLayoutStrategy : IToolModeMenuLayoutStrategy
{
    internal static SmithingHammerToolModeMenuLayoutStrategy Instance { get; } = new();

    private SmithingHammerToolModeMenuLayoutStrategy() { }

    /// <inheritdoc />
    public bool TryCreate(in ToolModeMenuLayoutContext context, out RadialMenuLayout? layout)
    {
        if (!CanCreate(context.Modes))
        {
            layout = null;
            return false;
        }

        // Multiples of four provide exact quarter-turn slots for Up, Right, Down, and Left.
        int wedgeCount = (context.Modes.Length + 3) / 4 * 4;
        var slots = new string?[wedgeCount];
        var actions = new List<string>();
        for (int index = 0; index < context.Modes.Length; index++)
        {
            int direction = GetUpsetDirection(context.Modes[index].Code?.Path);
            // Direction 0..3 maps to wedge 0, 1/4, 1/2, and 3/4 without changing the mode's selection ID.
            if (direction >= 0) slots[direction * wedgeCount / 4] = context.ModeIds[index];
            else actions.Add(context.ModeIds[index]);
        }

        // Preserve source order for non-directional actions, then materialize blank disabled wedges for stable spacing.
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
            context.Entries.Add(new RadialMenuEntry(paddingId, string.Empty, enabled: false));
        }

        layout = new RadialMenuLayout(Array.ConvertAll(slots, static id => id!),
            context.CurrentMenu.OuterRadius + 0.02, 1, context.CurrentMenu,
            separatorDegrees: 1.5, radiusScale: 0.60,
            sizeMultiplier: static () => VanillaExpandedModSystem.Config.ToolModeMenuSize);
        return true;
    }

    private static bool CanCreate(SkillItem[] modes)
    {
        bool hasDirectional = false;
        bool hasAction = false;
        var directions = new HashSet<int>();
        foreach (SkillItem mode in modes)
        {
            int direction = GetUpsetDirection(mode.Code?.Path);
            if (direction >= 0)
            {
                // Two modes cannot own the same cardinal wedge, so ambiguous modded sets use the generic layout.
                if (!directions.Add(direction)) return false;
                hasDirectional = true;
            }
            else hasAction = true;
        }
        // One center entry is added by the factory, leaving at most 63 shader-backed wedge entries.
        int wedgeCount = (modes.Length + 3) / 4 * 4;
        return hasDirectional && hasAction && wedgeCount <= 63;
    }

    private static int GetUpsetDirection(string? code) => code switch
    {
        "upsetup" => 0,
        "upsetright" => 1,
        "upsetdown" => 2,
        "upsetleft" => 3,
        _ => -1
    };
}
