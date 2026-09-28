using System.Collections.Generic;
using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Contains one validated radial representation of a collectible's tool modes.</summary>
internal sealed record ToolModeMenuContent(RadialMenuLayout Layout, IReadOnlyList<RadialMenuEntry> Entries);