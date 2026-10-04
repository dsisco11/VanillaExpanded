using VanillaExpanded.ItemSlotIndicators.Effects;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Pairs the winning provider's sampled presentation with its optional, immutable rendering effect.</summary>
/// <param name="Indicator">Cached fill and straight-alpha color supplied by the provider.</param>
/// <param name="Effect">Registration-owned effect metadata; null selects the ordinary rectangle.</param>
internal readonly record struct ItemSlotIndicatorRenderSelection(
    ItemSlotIndicator Indicator, ItemSlotIndicatorEffectDefinition? Effect)
{
    /// <summary>Gets an independently sampled presentation drawn after the primary indicator.</summary>
    internal ItemSlotIndicator? OverlayIndicator { get; init; }
    /// <summary>Gets the additional layer's registered effect.</summary>
    internal ItemSlotIndicatorEffectDefinition? OverlayEffect { get; init; }
}
