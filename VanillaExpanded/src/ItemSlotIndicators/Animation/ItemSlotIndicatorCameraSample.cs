using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Pairs copied orientation and optional bobbing eye height with camera identities used to detect discontinuities.</summary>
internal readonly record struct ItemSlotIndicatorCameraSample(
    object World, object Player, object Camera, EnumCameraMode Mode, ItemSlotIndicatorCameraBasis Basis,
    double? EyeHeight = null);
