using System;
using System.Linq;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.EcosystemMap.Acquisition;

/// <summary>Copies only actual delivered arrays; never samples APIs that substitute missing climate or generate terrain.</summary>
internal static class EnvironmentalSnapshots
{
    #region Public API
    /// <summary>Copies one original map reference and layout, rejecting absent, malformed or concurrently altered metadata.</summary>
    internal static EnvironmentalMap? Map(IntDataMap2D? source)
    {
        if (source == null) return null;
        int[]? values = source.Data;
        int size = source.Size, top = source.TopLeftPadding, bottom = source.BottomRightPadding;
        if (values == null || size <= 0 || top < 0 || bottom < 0 || (long)top + bottom >= size
            || (long)size * size > values.Length) return null;
        // Network decoding can retain a power-of-two backing buffer larger than the logical Size x Size map.
        // Preserve only logical cells; unused decoder capacity is not environmental data.
        var snapshot = new EnvironmentalMap(size, top, bottom, values.Take(checked(size * size)));
        // Vanilla replaces maps on packet receipt. Refuse a mixed reference/layout if a caller lacks stable ownership.
        return ReferenceEquals(values, source.Data) && size == source.Size
            && top == source.TopLeftPadding && bottom == source.BottomRightPadding ? snapshot : null;
    }

    /// <summary>Captures climate/forest independently; shrubs arrive through the feature's server delivery.</summary>
    internal static RegionalEnvironment Region(EcosystemRegionKey key, IMapRegion region) =>
        new(key, Map(region.ClimateMap), Map(region.ForestMap));

    /// <summary>Copies generated terrain heights, keeping missing or incorrectly sized maps unavailable.</summary>
    internal static TileEnvironment? Tile(EcosystemTileKey key, IMapChunk chunk, int chunkSize)
    {
        ushort[]? heights = chunk.WorldGenTerrainHeightMap;
        return heights != null && (long)chunkSize * chunkSize == heights.Length
            ? new TileEnvironment(key, chunkSize, heights) : null;
    }
    #endregion
}
