using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Narrow GL state boundary for upload scopes, allowing restoration checks without a graphics context.</summary>
internal interface IHabitatUploadApi
{
    /// <summary>Reads one unpack or texture binding value before any batch changes it.</summary>
    int GetInteger(GetPName name);
    /// <summary>Sets an unpack parameter on the current graphics context.</summary>
    void PixelStore(PixelStoreParameter name, int value);
    /// <summary>Changes the pixel-unpack-buffer binding only.</summary>
    void BindUnpackBuffer(int value);
    /// <summary>Restores the 2D texture binding on the unchanged active texture unit.</summary>
    void BindTexture(int value);
}
