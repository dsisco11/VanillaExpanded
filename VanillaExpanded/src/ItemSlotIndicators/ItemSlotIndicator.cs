using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>A bottom-up slot background with normalized fill and straight-alpha RGBA color.</summary>
internal readonly record struct ItemSlotIndicator(float Fill, Vector4 Color);