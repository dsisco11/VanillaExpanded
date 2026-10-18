using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Immutable original integer map values with their complete vanilla sampling layout.</summary>
internal sealed class EnvironmentalMap
{
    internal const int Version = 1;
    internal int Size { get; }
    internal int TopLeftPadding { get; }
    internal int BottomRightPadding { get; }
    internal IReadOnlyList<int> Values { get; }

    #region Public API
    /// <summary>Validates and copies a regional map without resampling or exposing the caller's mutable array.</summary>
    internal EnvironmentalMap(int size, int topLeftPadding, int bottomRightPadding, IEnumerable<int> values)
    {
        if (size <= 0 || topLeftPadding < 0 || bottomRightPadding < 0
            || (long)topLeftPadding + bottomRightPadding >= size) throw new ArgumentOutOfRangeException(nameof(size));
        int[] copy = values.ToArray();
        if ((long)size * size != copy.Length) throw new ArgumentException("Environmental map size does not match its values.", nameof(values));
        Size = size;
        TopLeftPadding = topLeftPadding;
        BottomRightPadding = bottomRightPadding;
        Values = Array.AsReadOnly(copy);
    }
    #endregion
}
