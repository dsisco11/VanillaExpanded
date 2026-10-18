using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Scopes the feature-owned database using vanilla's globally unique save identifier and the sampling geometry.</summary>
internal sealed record EcosystemStorageIdentity
{
    internal string SavegameIdentifier { get; }
    internal int WorldWidth { get; }
    internal int WorldHeight { get; }
    internal int WorldDepth { get; }
    internal int SeaLevel { get; }
    internal int RegionSize { get; }
    internal int ChunkSize { get; }

    #region Public API
    /// <summary>Rejects incomplete identity/geometry rather than sharing a fallback cache or accepting incompatible sampling.</summary>
    internal EcosystemStorageIdentity(string savegameIdentifier, int worldWidth, int worldHeight, int worldDepth, int seaLevel, int regionSize, int chunkSize)
    {
        if (string.IsNullOrWhiteSpace(savegameIdentifier)) throw new ArgumentException("A savegame identity is required.", nameof(savegameIdentifier));
        if (worldWidth <= 0 || worldDepth <= 0 || seaLevel <= 0 || worldHeight <= seaLevel || chunkSize <= 0 || regionSize < chunkSize || regionSize % chunkSize != 0)
            throw new ArgumentOutOfRangeException(nameof(worldHeight));
        SavegameIdentifier = savegameIdentifier;
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        WorldDepth = worldDepth;
        SeaLevel = seaLevel;
        RegionSize = regionSize;
        ChunkSize = chunkSize;
    }

    /// <summary>Chooses only the dedicated ecosystem directory; the identifier cannot inject a path or name a vanilla database.</summary>
    internal string GetDatabasePath(string dataPath) => Path.Combine(Path.GetFullPath(dataPath), "ModData", "vanillaexpanded", "ecosystem",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SavegameIdentifier))).ToLowerInvariant() + ".db");

    /// <summary>Associates a tile with its region, including consistent floor division for negative coordinates.</summary>
    internal EcosystemRegionKey RegionFor(EcosystemTileKey key)
    {
        int width = RegionSize / ChunkSize;
        return new EcosystemRegionKey(FloorDivide(key.X, width), FloorDivide(key.Z, width), key.Dimension);
    }
    #endregion

    #region Private
    /// <summary>Floors integer division so western/northern tiles map to the correct region.</summary>
    private static int FloorDivide(int value, int divisor) => value / divisor - (value % divisor < 0 ? 1 : 0);
    #endregion
}
