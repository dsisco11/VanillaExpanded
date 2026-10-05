namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Identifies the geometry used to present an indicator independently of its sampled value.</summary>
internal enum ItemSlotIndicatorRenderingStyle
{
    /// <summary>Bottom-up filled slot background.</summary>
    SlotBackground,
    /// <summary>Complete slot border colored by the provider.</summary>
    SlotOutline,
    /// <summary>Bottom-aligned horizontal track and proportional fill.</summary>
    HorizontalBar
}
