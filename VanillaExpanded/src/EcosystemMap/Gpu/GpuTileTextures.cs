using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns original generated heights and caller-supplied explored coverage independently of regional textures and species.</summary>
internal sealed class GpuTileTextures : IDisposable
{
    private GpuIntegerTexture? elevation, coverage;
    private readonly int size;
    private readonly bool available;

    #region Public API
    /// <summary>Uploads original unsigned heights plus the current per-column explored mask; no habitat pixels are computed.</summary>
    internal GpuTileTextures(TileEnvironment? terrain, int size, byte[] explored)
    {
        this.size = size;
        available = terrain != null && terrain.Size == size;
        try
        {
            elevation = new GpuIntegerTexture(available ? terrain!.Elevation.ToArray() : new ushort[checked(size * size)],
                size, PixelInternalFormat.R16ui, PixelType.UnsignedShort);
            UpdateCoverage(explored);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Replaces only the coverage texture when explored applicability changes, leaving environmental inputs intact.</summary>
    internal void UpdateCoverage(byte[] explored)
    {
        var replacement = new GpuIntegerTexture(explored, size, PixelInternalFormat.R8ui, PixelType.UnsignedByte);
        coverage?.Dispose(); coverage = replacement;
    }

    /// <summary>Binds source heights and explicit availability; missing terrain never becomes valid zero elevation.</summary>
    internal void Bind(IShaderProgram shader)
    {
        shader.BindTexture2D("terrainMap", elevation!.Id, 3);
        shader.BindTexture2D("exploredMap", coverage!.Id, 4);
        shader.Uniform("terrainAvailable", available ? 1 : 0);
    }

    /// <summary>Releases tile-owned textures without affecting shared regional resources.</summary>
    public void Dispose() { elevation?.Dispose(); coverage?.Dispose(); }
    #endregion
}
