using System;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one packed regional texture shared by explored terrain tiles and all species comparisons.</summary>
internal sealed class GpuRegionalTextures : IDisposable
{
    private readonly GpuHabitatTexture? texture;
    private readonly EnvironmentalMap?[] sources;
    private readonly int[] offsets;
    private static readonly string[] names = { "climate", "forest", "shrub" };

    #region Public API
    /// <summary>Uploads independent native grids in RGBA channels; unavailable components retain explicit zero-size layouts.</summary>
    internal GpuRegionalTextures(RegionalEnvironment? region, ILogger logger)
    {
        var packed = new PackedRegionalData(region, message => logger.Warning(message));
        sources = packed.Sources;
        offsets = packed.Offsets;
        texture = new GpuHabitatTexture(packed.Values, packed.Size,
            PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte);
    }

    /// <summary>Binds the regional input once and supplies each channel grid's original dimensions and padding.</summary>
    internal void Bind(IShaderProgram shader)
    {
        shader.BindTexture2D("regionalMap", texture!.Id, 0);
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            shader.Uniform(names[i] + "Layout", source?.Size ?? 0, source?.TopLeftPadding ?? 0, source?.BottomRightPadding ?? 0, offsets[i]);
        }
    }

    /// <summary>Releases only this region's packed texture on its creating graphics thread.</summary>
    public void Dispose() => texture?.Dispose();
    #endregion
}
