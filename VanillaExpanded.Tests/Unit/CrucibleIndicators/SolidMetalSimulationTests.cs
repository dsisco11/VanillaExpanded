using System.Numerics;
using VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;

namespace VanillaExpanded.Tests.Unit.CrucibleIndicators;

/// <summary>Checks independent metal pile initialization and movement contracts without a graphics context.</summary>
[Trait("Category", "Unit")]
public sealed class SolidMetalSimulationTests
{
    #region Public API
    /// <summary>Metal state is deterministic, profile-sized, contained, nonoverlapping, and at rest.</summary>
    [Fact]
    public void MetalInitialization_HasIndependentSizedSeparatedState()
    {
        var profile = GrainSimulationProfile.SolidMetal;
        var data = FoodGrainStateBuffers.CreateInitialState(profile.ParticleCount, profile.RadiusRange.X, profile.RadiusRange.Y);
        Assert.Equal(profile.ParticleCount * 8, data.Length);
        Assert.Equal(data, FoodGrainStateBuffers.CreateInitialState(profile.ParticleCount, profile.RadiusRange.X, profile.RadiusRange.Y));
        Assert.All(data, value => Assert.True(float.IsFinite(value)));
        for (int i = 0; i < profile.ParticleCount; i++)
        {
            int offset = i * 8;
            float r = data[offset + 6];
            Assert.True(r > 0);
            Assert.InRange(data[offset], r, 1 - r);
            Assert.InRange(data[offset + 1], r, 1 - r);
            Assert.Equal(0, data[offset + 2]);
            Assert.Equal(0, data[offset + 3]);
            for (int j = 0; j < i; j++)
            {
                float separation = Vector2.Distance(new(data[offset], data[offset + 1]), new(data[j * 8], data[j * 8 + 1]));
                Assert.True(separation >= r + data[j * 8 + 6]);
            }
        }
    }

    /// <summary>Food and metal remain independent, finite, bounded, and do not lift particles during falls.</summary>
    [Fact]
    public void MaterialForcing_KeepsFoodAndMetalIndependent()
    {
        var food = GrainSimulationProfile.Food;
        var metal = GrainSimulationProfile.SolidMetal;
        var foodForce = food.ProjectAcceleration(new(4, 3, 0));
        var metalForce = metal.ProjectAcceleration(new(4, 3, 0));
        Assert.True(float.IsFinite(foodForce.X) && float.IsFinite(foodForce.Y));
        Assert.True(float.IsFinite(metalForce.X) && float.IsFinite(metalForce.Y));
        Assert.True(metalForce.X > 0 && metalForce.Y > 0);
        Assert.Equal(0, metal.ProjectAcceleration(new(4, -100, 0)).Y);
        var extreme = metal.ProjectAcceleration(new(1000, 1000, 0));
        Assert.True(float.IsFinite(extreme.X) && float.IsFinite(extreme.Y));
        Assert.InRange(extreme.Length(), 0, metal.MaximumForce + 0.00001f);
        Assert.NotEqual(food.ShaderName, metal.ShaderName);
        Assert.True(food.ParticleCount > 0 && metal.ParticleCount > 0);
    }

    /// <summary>Food and solid metal settings control their independent simulation consumers.</summary>
    [Fact]
    public void Enablement_IsIndependent()
    {
        var config = VanillaExpandedModSystem.Config;
        string oldFood = config.FoodLevelIndicatorStyle;
        bool oldMetal = config.EnableCrucibleEffect;
        bool oldFreshness = config.EnablePerishableItemFreshnessIndicators, oldCrucible = config.EnableCrucibleIndicators;
        try
        {
            config.EnablePerishableItemFreshnessIndicators = config.EnableCrucibleIndicators = true;
            config.FoodLevelIndicatorStyle = "progress-bar";
            config.EnableCrucibleEffect = true;
            Assert.False(GrainSimulationProfile.Food.Enabled());
            Assert.True(GrainSimulationProfile.SolidMetal.Enabled());
            config.FoodLevelIndicatorStyle = "slot-background";
            config.EnableCrucibleEffect = false;
            Assert.True(GrainSimulationProfile.Food.Enabled());
            Assert.False(GrainSimulationProfile.SolidMetal.Enabled());
        }
        finally
        {
            config.FoodLevelIndicatorStyle = oldFood;
            config.EnableCrucibleEffect = oldMetal;
            config.EnablePerishableItemFreshnessIndicators = oldFreshness;
            config.EnableCrucibleIndicators = oldCrucible;
        }
    }

    /// <summary>Invalid state sizes cannot overrun the shader's borrowed feedback destination.</summary>
    [Fact]
    public void ShaderCount_IsBounded()
    {
        var shader = new FoodGrainSimulationShaderProgram { ParticleCount = 128 };
        Assert.Equal(128, shader.ParticleCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.ParticleCount = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => shader.ParticleCount = 385);
    }
    #endregion
}
