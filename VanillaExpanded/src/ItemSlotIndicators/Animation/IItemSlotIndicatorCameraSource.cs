namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Separates engine-specific camera access from shared animation math.</summary>
internal interface IItemSlotIndicatorCameraSource
{
    /// <summary>Copies rendered orientation and reset identities, returning null if no supported camera is available.</summary>
    ItemSlotIndicatorCameraSample? Capture();
}
