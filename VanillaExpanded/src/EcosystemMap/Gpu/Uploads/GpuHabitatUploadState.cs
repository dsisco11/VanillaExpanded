using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Captures and restores caller unpack/texture state once for a batch that actually performs uploads.</summary>
internal sealed class GpuHabitatUploadState : IDisposable
{
    private readonly IHabitatUploadApi api;
    private readonly int texture, alignment, rowLength, skipRows, skipPixels, buffer;
    private bool disposed;

    #region Public API
    /// <summary>Captures all values before setup; failed setup restores the complete captured state.</summary>
    internal GpuHabitatUploadState(IHabitatUploadApi api)
    {
        this.api = api;
        texture = api.GetInteger(GetPName.TextureBinding2D);
        alignment = api.GetInteger(GetPName.UnpackAlignment);
        rowLength = api.GetInteger(GetPName.UnpackRowLength);
        skipRows = api.GetInteger(GetPName.UnpackSkipRows);
        skipPixels = api.GetInteger(GetPName.UnpackSkipPixels);
        buffer = api.GetInteger(GetPName.PixelUnpackBufferBinding);
        try
        {
            // All buffers are contiguous CPU-owned arrays; both formats use this same transfer state.
            api.BindUnpackBuffer(0);
            api.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            api.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            api.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
            api.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Restores the caller's values once, including after a page allocation or partial-transfer failure.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.PixelStore(PixelStoreParameter.UnpackAlignment, alignment);
        api.PixelStore(PixelStoreParameter.UnpackRowLength, rowLength);
        api.PixelStore(PixelStoreParameter.UnpackSkipRows, skipRows);
        api.PixelStore(PixelStoreParameter.UnpackSkipPixels, skipPixels);
        api.BindUnpackBuffer(buffer);
        api.BindTexture(texture);
    }
    #endregion
}
