using System;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Maps resource fill onto a bounded top-edge level measured upward from the slot bottom.</summary>
internal readonly record struct ItemSlotIndicatorDrawRange
{
    /// <summary>Gets the slot fraction drawn at resource fill zero.</summary>
    internal float Minimum { get; }
    /// <summary>Gets the slot fraction drawn at resource fill one.</summary>
    internal float Maximum { get; }
    /// <summary>Gets whether this range is usable, including detection of an unconstructed default value.</summary>
    internal bool IsValid => float.IsFinite(Minimum) && float.IsFinite(Maximum)
        && Minimum >= 0 && Maximum <= 1 && Minimum < Maximum;

    #region Public API
    /// <summary>Creates ordered finite levels within the slot; an omitted range uses ordinary zero-to-one drawing.</summary>
    internal ItemSlotIndicatorDrawRange(float minimum, float maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
        if (!IsValid) throw new ArgumentOutOfRangeException(nameof(minimum), "Draw levels must satisfy 0 <= minimum < maximum <= 1.");
    }
    #endregion
}
