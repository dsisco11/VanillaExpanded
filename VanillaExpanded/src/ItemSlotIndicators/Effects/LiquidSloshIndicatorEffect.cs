using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Owns the shared liquid surface appearance and its visible empty/full draw levels.</summary>
internal static class LiquidSloshIndicatorEffect
{
    #region Public API
    /// <summary>Gets the sixteen-segment shader displaying the shared fluid grid with idle ripples and a meniscus.</summary>
    /// <remarks>Parameters are simulation gain divided by eight, idle wave weight, reserved zero, and meniscus weight.
    /// The draw shader corrects sampled mean and scales the combined profile to the common displacement bound.</remarks>
    internal static ItemSlotIndicatorEffectDefinition Definition { get; } = new(
        "vanillaexpanded:liquid-slosh", Constants.ModId, "vanillaexpanded_itemslot_liquid_slosh",
        parameters: new Vector4(0.625f, 0.125f, 0, 0.1875f), needsCameraMotion: true);

    /// <summary>Gets bottom-up average surface limits that retain liquid at empty and headroom at full.</summary>
    internal static ItemSlotIndicatorDrawRange DrawRange { get; } = new(0.15f, 0.85f);
    #endregion
}
