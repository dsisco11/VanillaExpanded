using System;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Defines immutable timing and change tolerances for optional adaptive sampling.</summary>
internal readonly record struct AdaptiveSamplingOptions
{
    #region Public API
    /// <summary>Creates the standard active interval, settling period, and change tolerances.</summary>
    public AdaptiveSamplingOptions() : this(100, 1_000, 0.005f, 0.01f)
    {
    }

    /// <summary>Creates validated settings for detecting activity and returning to idle sampling.</summary>
    internal AdaptiveSamplingOptions(long activeIntervalMilliseconds = 100, long settleMilliseconds = 1_000,
        float fillChangeThreshold = 0.005f, float colorChangeThreshold = 0.01f)
    {
        ActiveIntervalMilliseconds = activeIntervalMilliseconds;
        SettleMilliseconds = settleMilliseconds;
        FillChangeThreshold = fillChangeThreshold;
        ColorChangeThreshold = colorChangeThreshold;
        Validate();
    }

    /// <summary>Gets the interval while meaningful changes are occurring.</summary>
    internal long ActiveIntervalMilliseconds { get; }
    /// <summary>Gets the quiet period before returning to idle sampling.</summary>
    internal long SettleMilliseconds { get; }
    /// <summary>Gets the minimum significant fill difference between consecutive samples.</summary>
    internal float FillChangeThreshold { get; }
    /// <summary>Gets the minimum significant difference in any color component.</summary>
    internal float ColorChangeThreshold { get; }

    /// <summary>Rejects invalid values, including zero-initialized structs that bypass construction.</summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ActiveIntervalMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SettleMilliseconds);
        if (!float.IsFinite(FillChangeThreshold) || FillChangeThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(FillChangeThreshold));
        if (!float.IsFinite(ColorChangeThreshold) || ColorChangeThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(ColorChangeThreshold));
    }
    #endregion
}
