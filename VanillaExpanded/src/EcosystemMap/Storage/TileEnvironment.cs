using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Immutable original world-generation terrain heights for one map chunk; derived results do not belong in this record.</summary>
internal sealed class TileEnvironment
{
    internal const int Version = 1;
    internal EcosystemTileKey Key { get; }
    internal int Size { get; }
    internal IReadOnlyList<ushort> Elevation { get; }
    internal EnvironmentalCompleteness Completeness => EnvironmentalCompleteness.Elevation;

    #region Public API
    /// <summary>Copies and validates the tile's original elevation grid; coordinate association is validated by the storage identity.</summary>
    internal TileEnvironment(EcosystemTileKey key, int size, IEnumerable<ushort> elevation)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        ushort[] copy = elevation.ToArray();
        if ((long)size * size != copy.Length) throw new ArgumentException("Elevation size does not match its values.", nameof(elevation));
        Key = key;
        Size = size;
        Elevation = Array.AsReadOnly(copy);
    }
    #endregion
}
