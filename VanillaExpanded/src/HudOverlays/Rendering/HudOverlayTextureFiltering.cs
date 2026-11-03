using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Applies sampling appropriate to scaled or native-pixel HUD textures during preparation.</summary>
internal static class HudOverlayTextureFiltering
{
    #region Public API
    /// <summary>Builds a complete mip chain and selects trilinear minification while preserving the caller's texture binding.</summary>
    public static LoadedTexture Prepare(LoadedTexture texture) => Prepare(texture, nativePixels: false);

    /// <summary>Selects nearest-neighbor sampling without mipmaps for textures drawn at their native pixel size.</summary>
    public static LoadedTexture PrepareNativePixels(LoadedTexture texture) => Prepare(texture, nativePixels: true);
    #endregion

    private static LoadedTexture Prepare(LoadedTexture texture, bool nativePixels)
    {
        if (texture.TextureId == 0) return texture;
        // Work on the currently active unit without changing its selection or the surrounding shader state.
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            if (!nativePixels) GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)(nativePixels ? TextureMinFilter.Nearest : TextureMinFilter.LinearMipmapLinear));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)(nativePixels ? TextureMagFilter.Nearest : TextureMagFilter.Linear));
        }
        finally { GL.BindTexture(TextureTarget.Texture2D, previous); }
        return texture;
    }
}
