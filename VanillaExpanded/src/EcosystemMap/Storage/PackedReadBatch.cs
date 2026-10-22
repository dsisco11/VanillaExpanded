using System.Collections.Generic;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>A bounded group of independently nullable upload records from one storage epoch.</summary>
internal sealed record PackedReadBatch(
    IReadOnlyDictionary<EcosystemRegionKey, PackedRegionalData?> Regions,
    IReadOnlyDictionary<EcosystemTileKey, PackedTerrainData?> Tiles)
{
    internal const int MaximumRecords = 4;
}
