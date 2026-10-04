using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>A bottom-up slot background with resource fill, straight-alpha color, and optional bounded draw levels.</summary>
internal readonly record struct ItemSlotIndicator(float Fill, Vector4 Color, ItemSlotIndicatorDrawRange? DrawRange = null)
{
    /// <summary>Gets optional cached food-particle colors independently of the freshness background.</summary>
    internal ItemSlotIndicatorParticlePalette? ParticlePalette { get; init; }
}
