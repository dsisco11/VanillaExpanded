namespace VanillaExpanded.RadialMenu;

/// <summary>Controls whether a committed radial selection completes or continues the interaction.</summary>
public enum RadialMenuSelectionResult
{
    /// <summary>Completes the interaction and closes its dialog after mouse release.</summary>
    Close,
    /// <summary>Keeps the interaction open with caller-refreshed content.</summary>
    KeepOpen
}