using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one normalized regional or integer terrain atlas page on its creating graphics thread.</summary>
internal sealed class GpuHabitatTexture : IHabitatAtlasTexture
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly bool normalized;
    private readonly int width, height;
    public int Id { get; private set; }

    #region Public API
    /// <summary>Allocates one level-zero page; the atlas owner has already bounded its dimensions by hardware limits.</summary>
    internal GpuHabitatTexture(int width, int height, bool normalized)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        this.width = width; this.height = height; this.normalized = normalized;
        try
        {
            WithUploadState(() =>
            {
                Id = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, Id);
                GL.TexImage2D(TextureTarget.Texture2D, 0, normalized ? PixelInternalFormat.Rgba8 : PixelInternalFormat.Rg16ui,
                    width, height, 0, normalized ? PixelFormat.Rgba : PixelFormat.RgInteger,
                    normalized ? PixelType.UnsignedByte : PixelType.UnsignedShort, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)(normalized ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(normalized ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 0);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
            });
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Transfers only a background-prepared rectangle; no channel packing or CPU habitat evaluation occurs here.</summary>
    public void Upload(Array values, int size, int x, int y)
    {
        CheckThread();
        if (Id == 0 || size <= 0 || x < 0 || y < 0 || (long)x + size > width || (long)y + size > height
            || values.Length != checked(size * size * (normalized ? 4 : 2))
            || (normalized ? values is not byte[] : values is not ushort[])) throw new ArgumentException("Invalid atlas upload rectangle.");
        var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            WithUploadState(() =>
            {
                GL.BindTexture(TextureTarget.Texture2D, Id);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, size, size,
                    normalized ? PixelFormat.Rgba : PixelFormat.RgInteger,
                    normalized ? PixelType.UnsignedByte : PixelType.UnsignedShort, pinned.AddrOfPinnedObject());
            });
        }
        finally { pinned.Free(); }
    }
    /// <summary>Releases this page at the graphics boundary that allocated it.</summary>
    public void Dispose()
    {
        CheckThread();
        if (Id != 0) GL.DeleteTexture(Id);
        Id = 0;
    }
    #endregion

    #region Private
    /// <summary>Preserves the caller's unpack and texture state around a page allocation or partial transfer.</summary>
    private static void WithUploadState(Action upload)
    {
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        int alignment = GL.GetInteger(GetPName.UnpackAlignment), rowLength = GL.GetInteger(GetPName.UnpackRowLength);
        int skipRows = GL.GetInteger(GetPName.UnpackSkipRows), skipPixels = GL.GetInteger(GetPName.UnpackSkipPixels);
        int unpackBuffer = GL.GetInteger(GetPName.PixelUnpackBufferBinding);
        try
        {
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);
            upload();
        }
        finally
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, alignment);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, rowLength);
            GL.PixelStore(PixelStoreParameter.UnpackSkipRows, skipRows);
            GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, skipPixels);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, unpackBuffer);
            GL.BindTexture(TextureTarget.Texture2D, previous);
        }
    }
    /// <summary>Prevents GL use from background storage or input preparation.</summary>
    private void CheckThread()
    {
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("GPU page operations require the graphics thread.");
    }
    #endregion
}
