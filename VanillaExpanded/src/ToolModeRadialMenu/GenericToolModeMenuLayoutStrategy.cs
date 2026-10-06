using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Places every mode in one ring around the current-mode center.</summary>
internal sealed class GenericToolModeMenuLayoutStrategy : IToolModeMenuLayoutStrategy
{
    internal static GenericToolModeMenuLayoutStrategy Instance { get; } = new();

    private GenericToolModeMenuLayoutStrategy() { }

    /// <inheritdoc />
    public bool TryCreate(in ToolModeMenuLayoutContext context, out RadialMenuLayout? layout)
    {
        // Keep a small radial gap around the current-mode disc.
        layout = new RadialMenuLayout(context.ModeIds, context.CurrentMenu.OuterRadius + 0.02, 1, context.CurrentMenu,
            separatorDegrees: 1.0, radiusScale: 0.70,
            sizeMultiplier: static () => VanillaExpandedModSystem.Config.ToolModeMenuSize);
        return true;
    }
}
