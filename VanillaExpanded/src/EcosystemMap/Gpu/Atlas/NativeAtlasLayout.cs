using System;
using System.Collections.Generic;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Reserves fixed rows through the native allocator in logical slot units, with constant-time cell reuse.</summary>
internal sealed class NativeAtlasLayout
{
    private readonly long[] generations;
    private readonly Stack<int> free;
    private readonly int[] rowOrigins;
    private readonly int columns;
    private readonly int slotSize;
    private long generation;
    internal int Width { get; }
    internal int Height { get; }
    internal int Capacity => generations.Length;
    internal int Used => Capacity - free.Count;

    #region Public API
    /// <summary>Sizes an immutable page to retained demand within hardware limits, avoiding native full-pixel CPU storage.</summary>
    internal NativeAtlasLayout(int size, int demand, int hardwareLimit)
    {
        if (size <= 0 || demand <= 0 || hardwareLimit < size) throw new ArgumentOutOfRangeException(nameof(size));
        slotSize = size;
        // The captured 7,510-tile view fits one page below this provisional axis limit; extra pages handle growth.
        int axis = Math.Max(size, Math.Min(4096, hardwareLimit)) / size;
        int cells = (int)Math.Min(demand, (long)axis * axis);
        columns = Math.Min(axis, (int)Math.Ceiling(Math.Sqrt(cells)));
        int rows = (cells + columns - 1) / columns;
        Width = checked(columns * size); Height = checked(rows * size);
        generations = new long[checked(columns * rows)];
        free = new Stack<int>(generations.Length);
        rowOrigins = new int[rows];
        // Logical units bound the native constructor's temporary bitmap to cells, not pixels.
        var allocator = new TextureAtlas(columns, rows, 0, 0);
        allocator.DisposePixels();
        for (int row = 0; row < rows; row++)
        {
            var position = allocator.AllocateTextureSpace(row, columns, 1)
                ?? throw new NotSupportedException("Native atlas row allocation changed.");
            int y = (int)Math.Round(position.y1 * rows);
            if (position.x1 != 0 || position.x2 != 1 || y < 0 || y >= rows
                || Math.Abs(position.y1 * rows - y) > .001f
                || Math.Abs(position.y2 * rows - y - 1) > .001f)
                throw new NotSupportedException("Native atlas row bounds changed.");
            rowOrigins[row] = y * size;
        }
        // Native rows never relocate; the free list avoids a native tree walk for each terrain tile.
        for (int index = generations.Length - 1; index >= 0; index--) free.Push(index);
    }

    /// <summary>Claims one cell with a fresh generation; full pages require a separate page, never relocation.</summary>
    internal bool TryAllocate(out AtlasAddress address)
    {
        if (!free.TryPop(out int index)) { address = default; return false; }
        long token = checked(++generation);
        generations[index] = token;
        address = new(index, token, index % columns * slotSize, rowOrigins[index / columns], slotSize);
        return true;
    }
    /// <summary>Rejects released or recycled cell identities before resource publication or sampling.</summary>
    internal bool IsCurrent(AtlasAddress address) => address.Index >= 0 && address.Index < Capacity
        && address.Generation != 0 && generations[address.Index] == address.Generation;
    /// <summary>Recycles only the matching current generation without moving any retained cell.</summary>
    internal void Release(AtlasAddress address)
    {
        if (!IsCurrent(address)) return;
        generations[address.Index] = 0;
        free.Push(address.Index);
    }
    #endregion
}
