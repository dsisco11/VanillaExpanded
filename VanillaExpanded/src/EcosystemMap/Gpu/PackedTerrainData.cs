using System;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Packs exact generated heights and independent availability/category flags for an RG16UI upload.</summary>
internal static class PackedTerrainData
{
    internal const ushort HeightKnown = 1;
    internal const ushort KnownLand = 2;
    internal const ushort Ocean = 4;

    #region Public API
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
