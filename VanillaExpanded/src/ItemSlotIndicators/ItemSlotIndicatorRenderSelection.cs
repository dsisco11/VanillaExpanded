using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Pairs the winning provider's sampled presentation with its optional, immutable rendering effect.</summary>
/// <param name="Indicator">Cached fill and straight-alpha color supplied by the provider.</param>
/// <param name="Effect">Registration-owned effect metadata; null selects the ordinary rectangle.</param>
internal readonly record struct ItemSlotIndicatorRenderSelection(
    ItemSlotIndicator Indicator, ItemSlotIndicatorEffectDefinition? Effect);
