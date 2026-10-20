using System;
using System.Linq;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Packs native temperature/rainfall bytes and vegetation densities into aligned RGBA8 UNORM channel grids.</summary>
internal sealed class PackedRegionalData
{
    internal int Size { get; }
    internal byte[] Values { get; }
    internal int[] Offsets { get; }
    internal EnvironmentalMap?[] Sources { get; }

    #region Public API
    /// <summary>Copies each native grid without resampling; invalid densities remain unavailable instead of being narrowed.</summary>
    internal PackedRegionalData(RegionalEnvironment? region, Action<string>? reportUnsupported = null)
    {
        Sources = new[] { region?.Climate, ValidateDensity(region?.Forest, "forest", reportUnsupported),
            ValidateDensity(region?.Shrubs, "shrubs", reportUnsupported) };
        Offsets = new int[Sources.Length];
        // Equal-resolution grids share sample positions after padding alignment; preserve all original border cells.
        for (int i = 0; i < Sources.Length; i++)
        {
            var source = Sources[i];
            if (source == null) continue;
            int inner = source.Size - source.TopLeftPadding - source.BottomRightPadding;
            int leading = Sources.Where(other => other != null && other.Size - other.TopLeftPadding - other.BottomRightPadding == inner)
                .Max(other => other!.TopLeftPadding);
            Offsets[i] = leading - source.TopLeftPadding;
        }
        Size = Math.Max(1, Sources.Select((source, i) => source == null ? 0 : checked(source.Size + Offsets[i])).Max());
        Values = new byte[checked(Size * Size * 4)];
        // Copy original grids at their aligned physical offsets; native size/padding still bound logical samples.
        for (int component = 0; component < Sources.Length; component++)
        {
            var source = Sources[component];
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
    #endregion

    #region Private
    /// <summary>Reports a component-level unavailable input once during source upload, preserving all other regional maps.</summary>
    private static EnvironmentalMap? ValidateDensity(EnvironmentalMap? source, string name, Action<string>? report)
    {
        if (source == null || source.Values.All(value => value >= 0 && value <= 255)) return source;
        report?.Invoke($"Ecosystem {name} map contains a density outside 0–255; this component is unavailable for GPU evaluation.");
        return null;
    }
    #endregion
}
