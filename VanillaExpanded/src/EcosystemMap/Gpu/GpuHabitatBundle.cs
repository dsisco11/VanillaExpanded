using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Original environmental snapshots and world geometry, shared across all species selections without CPU comparisons.</summary>
internal sealed record GpuHabitatBundle(EcosystemTileKey Key, EcosystemStorageIdentity World,
    EnvironmentalInput<RegionalEnvironment> Region, EnvironmentalInput<TileEnvironment> Elevation);
