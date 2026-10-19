using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Immutable independently available generated heights and semantic ocean categories for one map chunk.</summary>
internal sealed class TileEnvironment
{
    internal const int Version = 2;
    internal EcosystemTileKey Key { get; }
    internal int Size { get; }
    internal IReadOnlyList<ushort> Elevation { get; }
    internal OceanMask? Ocean { get; }
    internal bool HasElevation => Elevation.Count != 0;
    internal EnvironmentalCompleteness Completeness => (HasElevation ? EnvironmentalCompleteness.Elevation : EnvironmentalCompleteness.None)
        | (Ocean != null ? EnvironmentalCompleteness.Ocean : EnvironmentalCompleteness.None);

    #region Public API
    /// <summary>Copies independently available height/category grids; coordinate association is validated by storage identity.</summary>
    internal TileEnvironment(EcosystemTileKey key, int size, IEnumerable<ushort>? elevation = null, OceanMask? ocean = null)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        ushort[] copy = elevation?.ToArray() ?? Array.Empty<ushort>();
        if (copy.Length != 0 && (long)size * size != copy.Length) throw new ArgumentException("Elevation size does not match its values.", nameof(elevation));
        if (ocean != null && ocean.Size != size) throw new ArgumentException("Ocean layout differs from tile.", nameof(ocean));
        Ocean = ocean;
        Key = key;
        Size = size;
        Elevation = Array.AsReadOnly(copy);
    }
    #endregion
}
