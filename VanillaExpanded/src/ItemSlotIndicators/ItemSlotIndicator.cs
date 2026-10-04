using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>A bottom-up slot background with resource fill, straight-alpha color, and optional bounded draw levels.</summary>
internal readonly record struct ItemSlotIndicator(float Fill, Vector4 Color, ItemSlotIndicatorDrawRange? DrawRange = null);
