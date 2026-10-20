using System;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Packs exact generated heights and independent availability/category flags for an RG16UI upload.</summary>
internal sealed class PackedTerrainData
{
    internal const ushort HeightKnown = 1;
    internal const ushort KnownLand = 2;
    internal const ushort Ocean = 4;

    internal int Size { get; }
    internal ushort[] Values { get; }
    internal bool HasElevation => (Values[1] & HeightKnown) != 0;

    #region Public API
    /// <summary>Owns worker-decoded interleaved words without copying or repacking them during publication.</summary>
    internal PackedTerrainData(int size, ushort[] values)
    {
        if (size <= 0 || (long)size * size * 2 != values.Length) throw new ArgumentException("Invalid packed terrain size.");
        Size = size; Values = values;
    }

    /// <summary>Writes interleaved height/flags; explored coverage belongs to the current chunk draw list, not cached texels.</summary>
    internal static ushort[] Pack(TileEnvironment? terrain, int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        int cells = checked(size * size);
        var values = new ushort[checked(cells * 2)];
        bool heights = terrain?.Size == size && terrain.HasElevation;
        var ocean = terrain?.Size == size ? terrain.Ocean : null;
        // Only copy source values and categories. Habitat evaluation remains entirely in the shader.
        for (int i = 0; i < cells; i++)
        {
            values[i * 2] = heights ? terrain!.Elevation[i] : (ushort)0;
            values[i * 2 + 1] = (ushort)((heights ? HeightKnown : 0) | ((ocean?.Columns[i] ?? 0) << 1));
        }
        return values;
    }
    #endregion
}
