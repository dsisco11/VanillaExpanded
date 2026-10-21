using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Adapts upload-state ownership to the current native GL context.</summary>
internal sealed class NativeHabitatUploadApi : IHabitatUploadApi
{
    #region Public API
    /// <summary>Reads unpack state or the current unit's 2D texture binding.</summary>
    public int GetInteger(GetPName name) => GL.GetInteger(name);
    /// <summary>Applies a specified unpack parameter.</summary>
    public void PixelStore(PixelStoreParameter name, int value) => GL.PixelStore(name, value);
    /// <summary>Binds a pixel-unpack buffer independently of array/element buffers.</summary>
    public void BindUnpackBuffer(int value) => GL.BindBuffer(BufferTarget.PixelUnpackBuffer, value);
    /// <summary>Restores the existing active unit without changing sampler or other target bindings.</summary>
    public void BindTexture(int value) => GL.BindTexture(TextureTarget.Texture2D, value);
    #endregion
}
