using VanillaExpanded.EcosystemMap.Catalogue;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>One active set of comparison ranges for the selected species; no GPU rule list or species buffer exists.</summary>
internal sealed record HabitatRanges(float MinTemperature, float MaxTemperature, float MinRainfall, float MaxRainfall,
    float MinForest, float MaxForest, float MinShrubs, float MaxShrubs, float MinElevation, float MaxElevation,
    float MinForestOrShrubs, HabitatSpawnPath Sampling)
{
    #region Public API
    /// <summary>Copies the chosen criteria set; it never unions or loops over catalogue alternatives.</summary>
    internal static HabitatRanges From(HabitatRule rule) => new(rule.MinTemperature, rule.MaxTemperature,
        rule.MinRainfall, rule.MaxRainfall, rule.MinForest, rule.MaxForest, rule.MinShrubs, rule.MaxShrubs,
        rule.MinElevation, rule.MaxElevation, rule.MinForestOrShrubs, rule.SpawnPath);

    /// <summary>Updates only comparison uniforms when the active species/ranges change, reusing environmental textures.</summary>
    internal void Apply(IShaderProgram program)
    {
        program.Uniform("temperatureRange", MinTemperature, MaxTemperature);
        program.Uniform("rainfallRange", MinRainfall, MaxRainfall);
        program.Uniform("forestRange", MinForest, MaxForest);
        program.Uniform("shrubRange", MinShrubs, MaxShrubs);
        program.Uniform("elevationRange", MinElevation, MaxElevation);
        program.Uniform("minForestOrShrubs", MinForestOrShrubs);
        program.Uniform("samplingPath", Sampling == HabitatSpawnPath.Worldgen ? 0 : 1);
    }
    #endregion
}
