using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Identifies reusable effect geometry independently of presentation and program identity.</summary>
internal readonly record struct ItemSlotIndicatorMeshKey(int AbiVersion, ItemSlotIndicatorTopology Topology, int SegmentCount)
{
    #region Public API
    /// <summary>Copies the validated geometry requirements from an immutable definition.</summary>
    internal static ItemSlotIndicatorMeshKey From(ItemSlotIndicatorEffectDefinition definition) =>
        new(definition.AbiVersion, definition.Topology, definition.SegmentCount);
    #endregion
}
