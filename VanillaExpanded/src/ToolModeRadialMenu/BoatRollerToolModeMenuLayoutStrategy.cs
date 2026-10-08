using System;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Places the boat roller's facing modes on their matching compass wedges.</summary>
internal sealed class BoatRollerToolModeMenuLayoutStrategy : IToolModeMenuLayoutStrategy
{
    internal static BoatRollerToolModeMenuLayoutStrategy Instance { get; } = new();

    private BoatRollerToolModeMenuLayoutStrategy() { }

    /// <inheritdoc />
    public bool TryCreate(in ToolModeMenuLayoutContext context, out RadialMenuLayout? layout)
    {
        if (context.Modes.Length != 4)
        {
            layout = null;
            return false;
        }

        var modeIdsByDirection = new string?[4];
        for (int index = 0; index < context.Modes.Length; index++)
        {
            int direction = GetDirection(context.Modes[index].Code?.Path);
            if (direction < 0 || modeIdsByDirection[direction] is not null)
            {
                layout = null;
                return false;
            }
            modeIdsByDirection[direction] = context.ModeIds[index];
        }

        if (Array.Exists(modeIdsByDirection, static id => id is null))
        {
            layout = null;
            return false;
        }

        // Wedge zero is north and indices advance clockwise: north, east, south, west.
        layout = new RadialMenuLayout(Array.ConvertAll(modeIdsByDirection, static id => id!),
            context.CurrentMenu.OuterRadius + RadialMenuWedgeStyle.RingGapFraction,
            ToolModeMenuGeometry.GetOuterRadius(context.CurrentMenu.OuterRadius + RadialMenuWedgeStyle.RingGapFraction, 0.50),
            context.CurrentMenu, separatorDegrees: 1.5, radiusScale: 0.60,
            sizeMultiplier: static () => VanillaExpandedModSystem.Config.ToolModeMenuSize);
        return true;
    }

    private static int GetDirection(string? code) => code switch
    {
        "north" => 0,
        "east" => 1,
        "south" => 2,
        "west" => 3,
        _ => -1
    };
}
