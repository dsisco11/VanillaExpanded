using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;

/// <summary>Declares independent granular state, shader identity, and camera forcing for each material.</summary>
internal sealed record GrainSimulationProfile(string ShaderName, int ParticleCount, Vector2 RadiusRange,
    float MotionGain, float MaximumForce, Func<bool> Enabled)
{
    #region Public API
    /// <summary>Gets vertical impulse gain independently of the horizontal movement response.</summary>
    internal float VerticalMotionGain { get; init; } = MotionGain;
    /// <summary>Gets food particle sizes with modest movement coupling independent of metal.</summary>
    internal static GrainSimulationProfile Food { get; } = new(
        FoodGrainSimulationShaderProgram.ShaderName, FoodGrainStateBuffers.ParticleCount, new(0.01f, 0.023f),
        0.30f, 6, static () => VanillaExpandedModSystem.Config.EnablePerishableItemFreshnessIndicators
            && VanillaExpandedModSystem.Config.FoodGrainEffectEnabled);
    /// <summary>Gets 128 larger chunks with restrained motion and independent high-friction contacts.</summary>
    internal static GrainSimulationProfile SolidMetal { get; } = new(
        "vanillaexpanded_itemslot_metal_chunks_simulation", 128, new(0.018f, 0.035f),
        0.6f, 6, static () => VanillaExpandedModSystem.Config.EnableCrucibleIndicators
            && VanillaExpandedModSystem.Config.EnableCrucibleEffect)
        {
            VerticalMotionGain = 0.75f
        };

    /// <summary>Projects generic motion into bounded granular impulses without lifting particles during falls.</summary>
    internal Vector2 ProjectAcceleration(Vector3 acceleration)
    {
        // Only compressive vertical acceleration excites the pile; downward acceleration never cancels settling.
        var force = new Vector2(acceleration.X * MotionGain, MathF.Max(0, acceleration.Y) * VerticalMotionGain);
        return force / MathF.Max(1, force.Length() / MaximumForce);
    }
    #endregion
}
