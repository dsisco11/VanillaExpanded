namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Identifies a successfully committed environmental update for targeted disposable cache invalidation.</summary>
internal readonly record struct EnvironmentalChange(EcosystemRegionKey? Region = null, EcosystemTileKey? Tile = null);
