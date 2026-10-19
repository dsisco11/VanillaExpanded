using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Off-view retention floors and queue bounds; the visible working set can expand retention without expanding job admission.</summary>
internal sealed record GpuHabitatLimits
{
    internal static GpuHabitatLimits Client { get; } = new(8, 128, 128, 4);
    internal int Regions { get; }
    internal int SourceTiles { get; }
    internal int Bundles { get; }
    internal int PendingJobs { get; }

    #region Public API
    /// <summary>Bounds retained original inputs without implying GPU timing or texture-memory acceptance.</summary>
    internal GpuHabitatLimits(int regions, int sourceTiles, int bundles, int pendingJobs)
    {
        if (regions <= 0 || sourceTiles <= 0 || bundles <= 0 || pendingJobs <= 0) throw new ArgumentOutOfRangeException(nameof(regions));
        Regions = regions; SourceTiles = sourceTiles; Bundles = bundles; PendingJobs = pendingJobs;
    }
    #endregion
}
