using System;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Retains one shared regional atlas slot and independent native channel layouts.</summary>
internal sealed class GpuRegionalTextures : IDisposable
{
    private readonly HabitatAtlasSlot slot;
    private readonly PackedMapLayout?[] layouts;
    private readonly int[] offsets;
    private readonly int size;
    private static readonly string[] names = { "climate", "forest", "shrub" };
    internal int TextureId => slot.TextureId;

    #region Public API
    /// <summary>Uploads one background-packed region without resampling its independent channel grids.</summary>
    internal GpuRegionalTextures(PackedRegionalData packed, HabitatAtlas atlas)
    {
        layouts = packed.Layouts; offsets = packed.Offsets; size = packed.Size;
        slot = atlas.Upload(packed.Values, packed.Size);
    }
    /// <summary>Updates layouts and pixel origin for a changed region, binding only when its atlas page changes.</summary>
    internal void Bind(IShaderProgram shader, bool bindTexture)
    {
        int texture = slot.TextureId;
        if (bindTexture) shader.BindTexture2D("regionalMap", texture, 0);
        shader.Uniform("regionalAtlasOrigin", (float)slot.Address.X, slot.Address.Y);
        shader.Uniform("regionalAtlasSize", (float)size);
        for (int i = 0; i < layouts.Length; i++)
        {
            var source = layouts[i];
            shader.Uniform(names[i] + "Layout", source?.Size ?? 0, source?.TopLeftPadding ?? 0, source?.BottomRightPadding ?? 0, offsets[i]);
        }
    }
    /// <summary>Releases the regional rectangle without disposing its shared page.</summary>
    public void Dispose() { slot.Dispose(); }
    #endregion
}
