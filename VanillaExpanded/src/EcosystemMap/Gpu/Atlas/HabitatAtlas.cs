using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns format-specific immutable pages, growing by pages while retained source slots remain stable.</summary>
internal sealed class HabitatAtlas : IDisposable
{
    private readonly List<HabitatAtlasPage> pages = new();
    private readonly Func<int, int, bool, IHabitatAtlasTexture> create;
    private readonly bool normalized;
    private readonly int maximum;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private int capacity;
    private bool disposed;
    internal int PageCount => pages.Count;
    internal long PayloadBytes => pages.Sum(page => (long)page.Layout.Width * page.Layout.Height * 4);
    internal int Used => pages.Sum(page => page.Layout.Used);
    internal int Slots => pages.Sum(page => page.Layout.Capacity);

    #region Public API
    /// <summary>Creates an isolated atlas owner; the factory supplies GL pages in production and recording pages in tests.</summary>
    internal HabitatAtlas(bool normalized, int maximum, int capacity, Func<int, int, bool, IHabitatAtlasTexture> create)
    {
        if (maximum <= 0 || capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.normalized = normalized; this.maximum = maximum; this.capacity = capacity; this.create = create;
    }
    /// <summary>Updates retained demand and disposes wholly empty pages without relocating live slots.</summary>
    internal void SetCapacity(int capacity)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        TrimEmpty();
    }
    /// <summary>Uploads only one new or changed source square and returns its immutable rectangle lease.</summary>
    internal HabitatAtlasSlot Upload(Array values, int size)
    {
        CheckThread();
        if (size <= 0 || size > maximum || values.Length != checked(size * size * (normalized ? 4 : 2))
            || (normalized ? values is not byte[] : values is not ushort[])) throw new ArgumentException("Invalid packed atlas input.");
        if (Used >= capacity) throw new InvalidOperationException("Atlas source retention exceeds its view capacity.");
        HabitatAtlasPage? selected = null;
        AtlasAddress address = default;
        foreach (var page in pages)
            if (page.SlotSize == size && page.Layout.TryAllocate(out address)) { selected = page; break; }
        if (selected == null)
        {
            TrimEmpty();
            selected = new(size, Math.Max(1, capacity - Used), maximum, normalized, create);
            pages.Add(selected);
            if (!selected.Layout.TryAllocate(out address)) throw new InvalidOperationException("New atlas page has no slots.");
        }
        try
        {
            selected.Texture.Upload(values, size, address.X, address.Y);
            return new(selected, address);
        }
        catch { selected.Layout.Release(address); TrimEmpty(); throw; }
    }
    /// <summary>Invalidates every outstanding cell and disposes all graphics pages for close or purge.</summary>
    internal void Clear()
    {
        CheckThread();
        foreach (var page in pages) page.Dispose();
        pages.Clear();
    }
    /// <summary>Releases all remaining pages on the creating graphics thread.</summary>
    public void Dispose() { Clear(); disposed = true; }
    #endregion

    #region Private
    /// <summary>Bounds empty-page retention after source eviction while leaving occupied page identities unchanged.</summary>
    private void TrimEmpty()
    {
        for (int i = pages.Count - 1; i >= 0; i--)
            if (pages[i].Layout.Used == 0) { pages[i].Dispose(); pages.RemoveAt(i); }
    }
    /// <summary>Prevents allocator and GL lifetime operations from crossing the graphics boundary.</summary>
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Atlas resources require their graphics thread.");
    }
    #endregion
}
