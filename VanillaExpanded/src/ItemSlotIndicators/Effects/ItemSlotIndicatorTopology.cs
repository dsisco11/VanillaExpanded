namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Identifies the reusable, indexed triangle geometry understood by indicator effects.</summary>
internal enum ItemSlotIndicatorTopology
{
    /// <summary>A single rectangular segment with bottom and surface vertices.</summary>
    Quad,
    /// <summary>Equal-width segments whose surface vertices can be deformed independently.</summary>
    FillStrip,
    /// <summary>Independent grain quads with corner coordinates in xy and particle identity in z.</summary>
    GrainQuads
}
