using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one nearest-sampled original integer texture, allocated and disposed only on its creating graphics thread.</summary>
internal sealed class GpuIntegerTexture : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    internal int Id { get; private set; }

    #region Public API
    /// <summary>Uploads original values without normalization, comparison or CPU pixel generation, restoring unpack/texture state.</summary>
    internal GpuIntegerTexture(Array values, int size, PixelInternalFormat format, PixelType type)
    {
        if (size <= 0 || values.Length != checked(size * size)) throw new ArgumentException("Invalid original texture layout.");
        if (size > GL.GetInteger(GetPName.MaxTextureSize)) throw new NotSupportedException("Environmental map exceeds GPU texture limits.");
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        int alignment = GL.GetInteger(GetPName.UnpackAlignment);
        int rowLength = GL.GetInteger(GetPName.UnpackRowLength);
        int skipRows = GL.GetInteger(GetPName.UnpackSkipRows), skipPixels = GL.GetInteger(GetPName.UnpackSkipPixels);
        int unpackBuffer = GL.GetInteger(GetPName.PixelUnpackBufferBinding);
        var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            Id = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, Id);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
            GL.TexImage2D(TextureTarget.Texture2D, 0, format, size, size, 0, PixelFormat.RedInteger, type, pinned.AddrOfPinnedObject());
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        catch { Dispose(); throw; }
        finally
        {
            pinned.Free();
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, alignment);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, rowLength);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, skipRows);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, skipPixels);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, unpackBuffer);
            GL.BindTexture(TextureTarget.Texture2D, previous);
        }
    }

    /// <summary>Releases this texture at the same graphics boundary that allocated it.</summary>
    public void Dispose()
    {
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("GPU texture disposal requires its graphics thread.");
        if (Id != 0) GL.DeleteTexture(Id);
        Id = 0;
    }
    #endregion
}
