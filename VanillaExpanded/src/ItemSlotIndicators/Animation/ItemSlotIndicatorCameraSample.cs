using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Pairs copied orientation with reference identities and camera mode used to detect discontinuities.</summary>
internal readonly record struct ItemSlotIndicatorCameraSample(
    object World, object Player, object Camera, EnumCameraMode Mode, ItemSlotIndicatorCameraBasis Basis);
