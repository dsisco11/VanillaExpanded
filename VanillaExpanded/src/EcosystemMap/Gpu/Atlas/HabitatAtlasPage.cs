using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns an immutable native-derived layout and one graphics page; live slots cannot survive page disposal.</summary>
internal sealed class HabitatAtlasPage : IDisposable
{
    internal NativeAtlasLayout Layout { get; }
    internal IHabitatAtlasTexture Texture { get; }
    internal int SlotSize { get; }
    internal bool Disposed { get; private set; }

    #region Public API
    /// <summary>Creates page storage only after its native layout is validated.</summary>
    internal HabitatAtlasPage(int size, int demand, int maximum, bool normalized, Func<int, int, bool, IHabitatAtlasTexture> create)
    {
        SlotSize = size;
        Layout = new(size, demand, maximum);
        Texture = create(Layout.Width, Layout.Height, normalized);
    }
    /// <summary>Invalidates every slot before releasing the graphics resource.</summary>
    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        Texture.Dispose();
    }
    #endregion
}
