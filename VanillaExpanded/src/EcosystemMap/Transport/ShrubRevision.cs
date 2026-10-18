using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Transport;

/// <summary>Computes a compact noncryptographic revision for original regional shrub inputs.</summary>
internal static class ShrubRevision
{
    #region Public API
    /// <summary>Hashes coordinates, sampling layout and every original integer value using 64-bit FNV-1a.</summary>
    internal static ulong Compute(EcosystemRegionKey key, EnvironmentalMap map)
    {
        ulong hash = 14695981039346656037UL;
        Add(ref hash, key.X);
        Add(ref hash, key.Z);
        Add(ref hash, key.Dimension);
        Add(ref hash, map.Size);
        Add(ref hash, map.TopLeftPadding);
        Add(ref hash, map.BottomRightPadding);
        Add(ref hash, map.Values.Count);
        foreach (int value in map.Values) Add(ref hash, value);
        return hash;
    }
    #endregion

    #region Private
    /// <summary>Mixes all four integer bytes in a defined order, preserving sign bits and intentional unsigned overflow.</summary>
    private static void Add(ref ulong hash, int value)
    {
        unchecked
        {
            uint bits = (uint)value;
            for (int shift = 0; shift < 32; shift += 8)
                hash = (hash ^ (byte)(bits >> shift)) * 1099511628211UL;
        }
    }
    #endregion
}
