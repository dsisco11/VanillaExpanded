using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Converts normalized elevation criteria into inclusive integer surface-height limits without sampling terrain on the CPU.</summary>
internal readonly record struct HabitatHeightRange(int Minimum, int Maximum)
{
    #region Public API
    /// <summary>Preserves vanilla double-precision elevation boundary semantics before GPU integer comparisons.</summary>
    internal static HabitatHeightRange From(float minimum, float maximum, int seaLevel, int worldHeight)
    {
        // Searching integer thresholds uses vanilla's exact division order, avoiding rounded float elevations on the GPU.
        int low = 0, high = worldHeight;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Normalize(middle, seaLevel, worldHeight) < minimum) low = middle + 1;
            else high = middle;
        }
        int first = low;
        low = 0; high = worldHeight;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (Normalize(middle, seaLevel, worldHeight) <= maximum) low = middle + 1;
            else high = middle;
        }
        return new(first, low - 1);
    }
    #endregion

    #region Private
    /// <summary>Uses the inspected server elevation convention for scalar range endpoints.</summary>
    private static double Normalize(int y, int seaLevel, int worldHeight) => y <= seaLevel
        ? (double)y / seaLevel : 1.0 + ((double)y - seaLevel) / (worldHeight - seaLevel);
    #endregion
}
