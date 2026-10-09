using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;

namespace VanillaExpanded.CrucibleIndicators;

/// <summary>Declares angular solid metal and a separately damped molten surface.</summary>
internal static class CrucibleIndicatorEffect
{
    #region Public API
    /// <summary>Gets cached grain geometry rendered from independent solid-metal state over a continuous fill.</summary>
    internal static ItemSlotIndicatorEffectDefinition Solid { get; } = new(
        "vanillaexpanded:crucible-solid", Constants.ModId, "vanillaexpanded_itemslot_crucible_solid",
        topology: ItemSlotIndicatorTopology.GrainQuads, segmentCount: FoodGrainStateBuffers.ParticleCount,
        parameters: new Vector4(0.95f / 0.55f, 0, 0, 0), needsCameraMotion: true, drawBackground: true);
    /// <summary>Gets the low-amplitude, sixteen-segment molten-metal surface.</summary>
    internal static ItemSlotIndicatorEffectDefinition Molten { get; } = new(
        "vanillaexpanded:crucible-molten", Constants.ModId, "vanillaexpanded_itemslot_crucible_molten",
        parameters: new Vector4(0.3f, 0, 0, 0.08f), needsCameraMotion: true);
    /// <summary>Gets visible amount bounds with room for motion at full capacity.</summary>
    internal static ItemSlotIndicatorDrawRange DrawRange { get; } = new(0.15f, 0.85f);
    #endregion
}
