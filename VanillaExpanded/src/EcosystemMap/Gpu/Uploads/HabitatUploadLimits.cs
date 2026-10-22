using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Per-frame admission limits; elapsed time is a soft stop between coherent resource admissions.</summary>
internal sealed record HabitatUploadLimits
{
    // A native terrain rectangle is 4 KiB; these provisional bounds cap a warm-page burst at 64 such transfers.
    internal static HabitatUploadLimits Client { get; } = new(256 * 1024, 64, TimeSpan.FromMilliseconds(2));
    internal long Bytes { get; }
    internal int Operations { get; }
    internal TimeSpan Elapsed { get; }

    #region Public API
    /// <summary>Requires finite positive limits; a first oversized receipt still progresses without splitting its resources.</summary>
    internal HabitatUploadLimits(long bytes, int operations, TimeSpan elapsed)
    {
        if (bytes <= 0 || operations <= 0 || elapsed <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(bytes));
        Bytes = bytes; Operations = operations; Elapsed = elapsed;
    }
    #endregion
}
