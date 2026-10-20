using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Retains one exact height/flags rectangle independently of species and regional allocations.</summary>
internal sealed class GpuTileTextures : IDisposable
{
    private readonly HabitatAtlasSlot slot;
    internal int TextureId => slot.TextureId;

    #region Public API
    /// <summary>Uploads background-prepared RG16UI words into one immutable atlas cell.</summary>
    internal GpuTileTextures(ushort[] packed, int size, HabitatAtlas atlas) { slot = atlas.Upload(packed, size); }
    /// <summary>Updates the exact terrain texel origin and binds the page only when it changes.</summary>
    internal void Bind(IShaderProgram shader, bool bindTexture)
    {
        int texture = slot.TextureId;
        if (bindTexture) shader.BindTexture2D("terrainMap", texture, 1);
        shader.Uniform("terrainAtlasOrigin", (float)slot.Address.X, slot.Address.Y);
    }
    /// <summary>Releases this cell without changing any other tile's page or address.</summary>
    public void Dispose() { slot.Dispose(); }
    #endregion
}
