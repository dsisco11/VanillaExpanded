namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Distinguishes a live composition from its frozen drag-source trace.</summary>
public enum NativeViewRole
{
    /// <summary>The current indicator view, including its moving destination preview during dragging.</summary>
    Live,
    /// <summary>A visual copy retained at the captured drag-source bounds without advancing animation independently.</summary>
    SourceTrace
}
