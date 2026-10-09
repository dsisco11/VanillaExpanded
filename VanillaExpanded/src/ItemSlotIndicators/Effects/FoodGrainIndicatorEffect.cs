using System.Numerics;
using VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Declares shared granular food rendering and persistent food-amount bounds.</summary>
internal static class FoodGrainIndicatorEffect
{
    #region Public API
    /// <summary>Gets the fixed grain-quad effect consuming the shared particle state.</summary>
    internal static ItemSlotIndicatorEffectDefinition Definition { get; } = new(
        "vanillaexpanded:food-grains", Constants.ModId, "vanillaexpanded_itemslot_food_grains",
        topology: ItemSlotIndicatorTopology.GrainQuads, segmentCount: FoodGrainStateBuffers.ParticleCount,
        parameters: new Vector4(0.95f / 0.35f, 0, 0, 0), needsCameraMotion: true);
    /// <summary>Gets food-amount bounds retaining grains and headroom at either endpoint.</summary>
    internal static ItemSlotIndicatorDrawRange DrawRange { get; } = new(0.2f, 0.85f);
    #endregion
}
