using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Plans immutable page dimensions without allocating cells, native bitmap data or graphics resources.</summary>
internal readonly record struct AtlasPageGeometry(int Columns, int Rows, int Width, int Height)
{
    #region Public API
    /// <summary>Uses retained demand and the same hardware-bounded whole-row policy for estimates and real allocation.</summary>
    internal static AtlasPageGeometry For(int size, int demand, int hardwareLimit)
    {
        if (size <= 0 || demand <= 0 || hardwareLimit < size) throw new ArgumentOutOfRangeException(nameof(size));
        int axis = Math.Max(size, Math.Min(4096, hardwareLimit)) / size;
        int cells = (int)Math.Min(demand, (long)axis * axis);
        int columns = Math.Min(axis, (int)Math.Ceiling(Math.Sqrt(cells)));
        int rows = (cells + columns - 1) / columns;
        return new(columns, rows, checked(columns * size), checked(rows * size));
    }
    #endregion
}
