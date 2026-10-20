using System;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one packed height/flags texture independently of regional sources and species selection.</summary>
internal sealed class GpuTileTextures : IDisposable
{
    private readonly GpuHabitatTexture? texture;

    #region Public API
    /// <summary>Uploads background-prepared height/flag words without packing channels or computing habitat pixels.</summary>
    internal GpuTileTextures(ushort[] packed, int size)
    {
        texture = new GpuHabitatTexture(packed, size,
            PixelInternalFormat.Rg16ui, PixelFormat.RgInteger, PixelType.UnsignedShort);
    }

    /// <summary>Binds the packed tile input; its flags keep missing height distinct from valid zero height.</summary>
    internal void Bind(IShaderProgram shader) => shader.BindTexture2D("terrainMap", texture!.Id, 1);

    /// <summary>Releases this tile's single texture without affecting shared regional resources.</summary>
    public void Dispose() => texture?.Dispose();
    #endregion
}
