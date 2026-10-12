using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Distributes modes across bounded concentric rings around the current-mode center.</summary>
internal sealed class GenericToolModeMenuLayoutStrategy : IToolModeMenuLayoutStrategy
{
    internal static GenericToolModeMenuLayoutStrategy Instance { get; } = new();

    /// <summary>Limits angular crowding without adding navigation or changing selection identifiers.</summary>
    internal const int MaximumModesPerRing = 12;

    /// <summary>Creates the shared generic layout strategy.</summary>
    private GenericToolModeMenuLayoutStrategy() { }

    #region Public API
    /// <inheritdoc />
    public bool TryCreate(in ToolModeMenuLayoutContext context, out RadialMenuLayout? layout)
    {
        // Balance contiguous slices so the final ring is not left with one oversized wedge.
        // Every ring retains its thickness: adding choices grows the menu rather than shrinking its icons.
        int ringCount = (context.ModeIds.Length + MaximumModesPerRing - 1) / MaximumModesPerRing;
        layout = context.CurrentMenu;
        int start = 0;
        for (int ring = 0; ring < ringCount; ring++)
        {
            int count = context.ModeIds.Length / ringCount + (ring < context.ModeIds.Length % ringCount ? 1 : 0);
            var ids = new string[count];
            for (int index = 0; index < count; index++) ids[index] = context.ModeIds[start + index];
            start += count;
            double innerRadius = layout.OuterRadius + RadialMenuWedgeStyle.RingGapFraction;
            layout = new RadialMenuLayout(ids, innerRadius, ToolModeMenuGeometry.GetOuterRadius(innerRadius), layout,
                separatorDegrees: ToolModeMenuGeometry.GenericSeparatorDegrees, radiusScale: ToolModeMenuGeometry.DefaultRadiusScale,
                sizeMultiplier: static () => VanillaExpandedModSystem.Config.ToolModeMenuSize);
        }
        return true;
    }
    #endregion
}
