using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns original regional textures shared by visible terrain tiles and all active species comparisons.</summary>
internal sealed class GpuRegionalTextures : IDisposable
{
    private readonly GpuIntegerTexture?[] maps = new GpuIntegerTexture?[3];
    private readonly EnvironmentalMap?[] sources;
    private static readonly string[] names = { "climate", "forest", "shrub" };

    #region Public API
    /// <summary>Uploads the three original maps once; absent maps use a harmless texture with an explicit unavailable layout.</summary>
    internal GpuRegionalTextures(RegionalEnvironment? region)
    {
        sources = new[] { region?.Climate, region?.Forest, region?.Shrubs };
        try
        {
            for (int i = 0; i < maps.Length; i++)
                maps[i] = new GpuIntegerTexture(sources[i]?.Values.ToArray() ?? new int[1], sources[i]?.Size ?? 1,
                    PixelInternalFormat.R32i, PixelType.Int);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Binds shared regional values and independent layouts without resampling or selecting species.</summary>
    internal void Bind(IShaderProgram shader)
    {
        for (int i = 0; i < maps.Length; i++)
        {
            var source = sources[i];
            shader.BindTexture2D(names[i] + "Map", maps[i]!.Id, i);
            shader.Uniform(names[i] + "Layout", source?.Size ?? 0, source?.TopLeftPadding ?? 0, source?.BottomRightPadding ?? 0);
        }
    }

    /// <summary>Releases only this region's owned textures on the graphics thread.</summary>
    public void Dispose() { foreach (var map in maps) map?.Dispose(); }
    #endregion
}
