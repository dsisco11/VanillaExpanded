using System;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Packs native temperature/rainfall bytes and vegetation densities into aligned RGBA8 UNORM channel grids.</summary>
internal sealed class PackedRegionalData
{
    internal int Size { get; }
    internal byte[] Values { get; }
    internal int[] Offsets { get; }
    internal PackedMapLayout?[] Layouts { get; }

    #region Public API
    /// <summary>Copies each native grid without resampling; invalid densities remain unavailable instead of being narrowed.</summary>
    internal PackedRegionalData(RegionalEnvironment? region, Action<string>? reportUnsupported = null)
    {
        var sources = new[] { region?.Climate, ValidateDensity(region?.Forest, "forest", reportUnsupported),
            ValidateDensity(region?.Shrubs, "shrubs", reportUnsupported) };
        Layouts = sources.Select(source => source == null ? null : new PackedMapLayout(source.Size, source.TopLeftPadding, source.BottomRightPadding)).ToArray();
        Offsets = new int[sources.Length];
        // Equal-resolution grids share sample positions after padding alignment; preserve all original border cells.
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            if (source == null) continue;
            int inner = source.Size - source.TopLeftPadding - source.BottomRightPadding;
            int leading = sources.Where(other => other != null && other.Size - other.TopLeftPadding - other.BottomRightPadding == inner)
                .Max(other => other!.TopLeftPadding);
            Offsets[i] = leading - source.TopLeftPadding;
        }
        Size = Math.Max(1, sources.Select((source, i) => source == null ? 0 : checked(source.Size + Offsets[i])).Max());
        Values = new byte[checked(Size * Size * 4)];
        // Copy original grids at their aligned physical offsets; native size/padding still bound logical samples.
        for (int component = 0; component < sources.Length; component++)
        {
            var source = sources[component];
            if (source == null) continue;
            for (int z = 0; z < source.Size; z++)
            for (int x = 0; x < source.Size; x++)
            {
                int value = source.Values[z * source.Size + x];
                int offset = ((z + Offsets[component]) * Size + x + Offsets[component]) * 4;
                if (component == 0)
                {
                    // Climate's unused low byte does not participate in habitat comparisons.
                    Values[offset] = (byte)((value >> 16) & 255);
                    Values[offset + 1] = (byte)((value >> 8) & 255);
                }
                else Values[offset + component + 1] = (byte)value;
            }
        }
    }
    /// <summary>Restores worker-decoded upload bytes and checked layout metadata without repacking any channels.</summary>
    internal PackedRegionalData(int size, byte[] values, PackedMapLayout?[] layouts, int[] offsets)
    {
        Size = size; Values = values; Layouts = layouts; Offsets = offsets;
    }
    #endregion

    #region Private
    /// <summary>Reports a component-level unavailable input during background channel packing, preserving all other regional maps.</summary>
    private static EnvironmentalMap? ValidateDensity(EnvironmentalMap? source, string name, Action<string>? report)
    {
        if (source == null || source.Values.All(value => value >= 0 && value <= 255)) return source;
        report?.Invoke($"Ecosystem {name} map contains a density outside 0–255; this component is unavailable for GPU evaluation.");
        return null;
    }
    #endregion
}
