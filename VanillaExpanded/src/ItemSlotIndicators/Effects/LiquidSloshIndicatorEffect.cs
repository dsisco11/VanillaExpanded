using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Owns the shared liquid surface appearance and its visible empty/full draw levels.</summary>
internal static class LiquidSloshIndicatorEffect
{
    #region Public API
    /// <summary>Gets the shared sixteen-segment liquid shader with bounded standing-wave, tilt, and meniscus weights.</summary>
    /// <remarks>Parameters are resting wave weight, extra motion wave weight, tilt weight, and meniscus weight.
    /// Nonnegative displacement weights sum to one; the shader repeats every two seconds.</remarks>
    internal static ItemSlotIndicatorEffectDefinition Definition { get; } = new(
        "vanillaexpanded:liquid-slosh", Constants.ModId, "vanillaexpanded_itemslot_liquid_slosh",
        parameters: new Vector4(0.625f, 0.125f, 0.0625f, 0.1875f), needsCameraMotion: true);

    /// <summary>Gets bottom-up average surface limits that retain liquid at empty and headroom at full.</summary>
    internal static ItemSlotIndicatorDrawRange DrawRange { get; } = new(0.15f, 0.85f);
    #endregion
}
