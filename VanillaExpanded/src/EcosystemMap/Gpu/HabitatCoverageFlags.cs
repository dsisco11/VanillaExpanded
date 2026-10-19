using System;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Packs raw exploration and ocean category bits into one integer upload without evaluating habitat.</summary>
internal static class HabitatCoverageFlags
{
    #region Public API
    /// <summary>Uses bit 0 for exploration, bit 1 for known non-ocean and bit 2 for confirmed ocean.</summary>
    internal static byte[] Pack(byte[] explored, OceanMask? ocean, int size)
    {
        if (size <= 0 || (long)size * size != explored.Length || (ocean != null && ocean.Size != size))
            throw new ArgumentException("Invalid tile coverage layout.", nameof(explored));
        var values = new byte[explored.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = (byte)((explored[i] != 0 ? 1 : 0) | ((ocean?.Columns[i] ?? 0) << 1));
        return values;
    }
    #endregion
}
