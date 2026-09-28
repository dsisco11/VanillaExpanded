using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Builds radial geometry for one family of collectible tool modes.</summary>
internal interface IToolModeMenuLayoutStrategy
{
    /// <summary>Builds specialized geometry, or declines so the generic strategy can handle the modes.</summary>
    bool TryCreate(in ToolModeMenuLayoutContext context, out RadialMenuLayout? layout);
}