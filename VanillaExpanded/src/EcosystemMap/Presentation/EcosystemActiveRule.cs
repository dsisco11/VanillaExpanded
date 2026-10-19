using System;
using System.Linq;
using VanillaExpanded.EcosystemMap.Catalogue;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Selects one ongoing-spawn definition, falling back to worldgen, without widening habitat ranges.</summary>
internal static class EcosystemActiveRule
{
    #region Public API
    /// <summary>Uses ordinal entity identity to make representative selection stable within the preferred spawning path.</summary>
    internal static HabitatRule Select(EcosystemSpecies species)
    {
        // Discovery guarantees at least one complete alternative. Probability, sex and age do not add dropdown rows.
        return species.Alternatives.OrderBy(rule => rule.SpawnPath == HabitatSpawnPath.Runtime ? 0 : 1)
            .ThenBy(rule => rule.EntityCode, StringComparer.Ordinal).First();
    }
    #endregion
}
