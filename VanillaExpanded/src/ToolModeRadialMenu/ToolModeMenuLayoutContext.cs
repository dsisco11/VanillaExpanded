using System.Collections.Generic;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Provides validated mode content to a geometry-only layout strategy.</summary>
internal sealed record ToolModeMenuLayoutContext(
    SkillItem[] Modes,
    string[] ModeIds,
    List<RadialMenuEntry> Entries,
    RadialMenuLayout CurrentMenu);