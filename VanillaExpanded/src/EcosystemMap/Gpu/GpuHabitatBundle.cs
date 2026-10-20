using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Versioned prepacked environmental inputs and world geometry, shared across all species selections without CPU comparisons.</summary>
internal sealed record GpuHabitatBundle(EcosystemTileKey Key, EcosystemStorageIdentity World,
    EnvironmentalInput<PackedRegionalData> Region, EnvironmentalInput<PackedTerrainData> Elevation)
{
    internal PackedRegionalData? RegionalTexture { get; init; }
    internal ushort[]? TerrainTexture { get; init; }
}
