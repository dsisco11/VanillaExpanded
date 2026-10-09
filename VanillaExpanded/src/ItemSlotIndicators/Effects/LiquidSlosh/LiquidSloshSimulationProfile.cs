using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Defines independent solver tuning, engine registration identity, and live enablement.</summary>
internal sealed record LiquidSloshSimulationProfile(string ShaderName, float Gravity, float Damping, Func<bool> Enabled)
{
    #region Public API
    /// <summary>Gets horizontal and vertical impulse gains relative to the default liquid response.</summary>
    internal Vector2 AccelerationScale { get; init; } = Vector2.One;
    /// <summary>Gets the relative strength of irregular vertical-impact jostling.</summary>
    internal float JostleScale { get; init; } = 1;
    /// <summary>Gets the maximum combined acceleration in solver units, bounding abrupt movement stops.</summary>
    internal float MaximumAcceleration { get; init; } = 3;
    /// <summary>Gets the existing water simulation's settings.</summary>
    internal static LiquidSloshSimulationProfile Water { get; } = new(
        LiquidSloshSimulationShaderProgram.ShaderName, 1, 3,
        static () => VanillaExpandedModSystem.Config.EnableLiquidContainerIndicators
            && VanillaExpandedModSystem.Config.EnableLiquidSloshEffect);
    /// <summary>Gets a cohesive, strongly damped metal response with restrained impacts and vertical jostling.</summary>
    internal static LiquidSloshSimulationProfile Metal { get; } = new(
        "vanillaexpanded_itemslot_metal_simulation", 1, 10,
        static () => VanillaExpandedModSystem.Config.EnableCrucibleIndicators
            && VanillaExpandedModSystem.Config.EnableCrucibleEffect)
        {
            AccelerationScale = new(0.65f, 0.2f),
            JostleScale = 0.15f,
            MaximumAcceleration = 1.2f
        };
    #endregion
}
