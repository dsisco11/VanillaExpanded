namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Distinguishes native switches, embedded Style choices, and action buttons.</summary>
internal enum HudMenuEntryKind
{
    /// <summary>A configured enable or effective lock switch.</summary>
    Toggle,
    /// <summary>An enum-backed choice belonging to the Style dropdown.</summary>
    StyleChoice,
    /// <summary>A native button invoking a target-owned action.</summary>
    Action
}
