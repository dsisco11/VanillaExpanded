using System;
using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Separates fixed chisel actions from dynamic material choices.</summary>
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

        string[] actionIds = context.ModeIds[..materialStart];
        string[] materialIds = context.ModeIds[materialStart..];
        // Materials occupy the inner ring while the factory-owned current-mode disc remains at the center.
        var materialMenu = new RadialMenuLayout(materialIds, 0.30, 0.56, context.CurrentMenu,
            separatorDegrees: 1.5);
        layout = new RadialMenuLayout(actionIds, 0.62, 1, materialMenu,
            separatorDegrees: 1.5, radiusScale: 0.6);
        return true;
    }
}