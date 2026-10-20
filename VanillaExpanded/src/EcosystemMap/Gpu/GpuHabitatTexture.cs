using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Owns one normalized regional or integer terrain texture on its creating graphics thread.</summary>
internal sealed class GpuHabitatTexture : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    internal int Id { get; private set; }

    #region Public API
    /// <summary>Uploads original bytes or ushort values with the matching format/filtering contract, restoring unpack and texture state.</summary>
    internal GpuHabitatTexture(Array values, int size, PixelInternalFormat format, PixelFormat channels, PixelType type)
    {
        int components = channels switch
        {
            PixelFormat.Rgba => 4,
            PixelFormat.RgInteger => 2,
            _ => throw new ArgumentException("Unsupported packed habitat channels.", nameof(channels))
        };
        bool normalized = format == PixelInternalFormat.Rgba8 && channels == PixelFormat.Rgba && type == PixelType.UnsignedByte && values is byte[];
        bool integer = format == PixelInternalFormat.Rg16ui && channels == PixelFormat.RgInteger && type == PixelType.UnsignedShort && values is ushort[];
        if (!normalized && !integer) throw new ArgumentException("Unsupported habitat texture format or value type.", nameof(format));
        if (size <= 0 || values.Length != checked(size * size * components)) throw new ArgumentException("Invalid original texture layout.");
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
            GL.TexImage2D(TextureTarget.Texture2D, 0, format, size, size, 0, channels, type, pinned.AddrOfPinnedObject());
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)(normalized ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(normalized ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
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
