using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one generation-stamped atlas rectangle, not its shared texture page.</summary>
internal sealed class HabitatAtlasSlot : IDisposable
{
    private readonly HabitatAtlasPage page;
    private bool disposed;
    internal AtlasAddress Address { get; }
    internal bool IsCurrent => !disposed && !page.Disposed && page.Layout.IsCurrent(Address);
    internal int TextureId => IsCurrent ? page.Texture.Id : throw new InvalidOperationException("Atlas allocation is stale.");

    #region Public API
    /// <summary>Retains the exact page and cell generation returned by allocation.</summary>
    internal HabitatAtlasSlot(HabitatAtlasPage page, AtlasAddress address) { this.page = page; Address = address; }
    /// <summary>Releases the cell only once; a recycled cell remains owned by its newer generation.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!page.Disposed) page.Layout.Release(Address);
    }
    #endregion
}
