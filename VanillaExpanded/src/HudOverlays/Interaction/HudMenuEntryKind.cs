namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Distinguishes native switch rows from choices in the embedded Style dropdown.</summary>
internal enum HudMenuEntryKind
{
    /// <summary>A configured enable or effective lock switch.</summary>
    Toggle,
    /// <summary>An enum-backed choice belonging to the Style dropdown.</summary>
    StyleChoice
}
