using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>Identifies the vanilla sampling convention attached to a complete environmental alternative.</summary>
internal enum HabitatSpawnPath { Worldgen, Runtime }

/// <summary>Immutable environmental limits from one entity and one spawning path; unrelated spawn checks are excluded.</summary>
internal sealed record HabitatRule(
    string EntityCode, HabitatSpawnPath SpawnPath,
    float MinTemperature, float MaxTemperature, float MinRainfall, float MaxRainfall,
    float MinForest, float MaxForest, float MinShrubs, float MaxShrubs,
    float MinForestOrShrubs, float MinElevation, float MaxElevation)
{
    #region Public API
    /// <summary>Copies only the approved habitat fields, retaining provenance without aliasing mutable game conditions.</summary>
    internal static HabitatRule From(string entityCode, HabitatSpawnPath path, ClimateSpawnCondition conditions) =>
        new(entityCode, path, conditions.MinTemp, conditions.MaxTemp, conditions.MinRain, conditions.MaxRain,
            conditions.MinForest, conditions.MaxForest, conditions.MinShrubs, conditions.MaxShrubs,
            conditions.MinForestOrShrubs, conditions.MinY, conditions.MaxY);
    #endregion
}
