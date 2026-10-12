namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Exposes retained presentation lifetime to the shared layout and rendering runtime.</summary>
internal interface IHudOverlayVisibilityTransition
{
    /// <summary>Gets the transition controlling the currently prepared presentation.</summary>
    HudOverlayVisibilityTransition Visibility { get; }
    /// <summary>Retains the last preparation until its exit finishes.</summary>
    void HidePresentation();
}
