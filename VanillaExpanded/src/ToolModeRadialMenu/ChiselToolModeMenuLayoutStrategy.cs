using System;
using System.Collections.Generic;
using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Places chisel sizes inside all non-size actions and material choices.</summary>
internal sealed class ChiselToolModeMenuLayoutStrategy : IToolModeMenuLayoutStrategy
{
    internal static ChiselToolModeMenuLayoutStrategy Instance { get; } = new();

    private ChiselToolModeMenuLayoutStrategy() { }

    /// <inheritdoc />
    public bool TryCreate(in ToolModeMenuLayoutContext context, out RadialMenuLayout? layout)
    {
        // Vanilla starts its material row with Linebreak; this avoids depending on a fixed action count or material codes.
        int materialStart = Array.FindIndex(context.Modes, static mode => mode.Linebreak);
        if (materialStart <= 0 || materialStart >= context.Modes.Length)
        {
            layout = null;
            return false;
        }

        // The base skill grid invokes disabled tiles as drop targets. The radial flow makes only Add Material actionable.
        int addMaterial = Array.FindIndex(context.Modes,
            static mode => mode.Code?.Path == "addmat");
        if (addMaterial >= 0)
        {
            RadialMenuEntry entry = context.Entries[addMaterial];
            context.Entries[addMaterial] = new RadialMenuEntry(entry.Id, entry.Label, enabled: true,
                entry.Icon, entry.Description);
        }

        var sizeIds = new List<string>(4);
        var outerIds = new List<string>(context.ModeIds.Length - 4);
        for (int index = 0; index < context.Modes.Length; index++)
        {
            if (IsSizeMode(context.Modes[index].Code?.Path)) sizeIds.Add(context.ModeIds[index]);
            else outerIds.Add(context.ModeIds[index]);
        }
        if (sizeIds.Count != 4 || outerIds.Count == 0)
        {
            layout = null;
            return false;
        }

        // The four carving sizes surround the current-mode disc; every other option remains easy to reach outside.
        var sizeMenu = new RadialMenuLayout(sizeIds, context.CurrentMenu.OuterRadius + 0.02, 0.58, context.CurrentMenu,
            separatorDegrees: 1.5);
        layout = new RadialMenuLayout(outerIds, 0.60, 1, sizeMenu,
            separatorDegrees: 1.5, radiusScale: 0.60,
            sizeMultiplier: static () => VanillaExpandedModSystem.Config.ToolModeMenuSize);
        return true;
    }

    private static bool IsSizeMode(string? code) => code is "1size" or "2size" or "4size" or "8size";
}
