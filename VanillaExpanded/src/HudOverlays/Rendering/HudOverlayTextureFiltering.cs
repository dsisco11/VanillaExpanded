using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Applies consistent mipmapped sampling to HUD textures at their preparation boundary.</summary>
internal static class HudOverlayTextureFiltering
{
    #region Public API
    /// <summary>Builds a complete mip chain and selects trilinear minification while preserving the caller's texture binding.</summary>
    public static LoadedTexture Prepare(LoadedTexture texture)
    {
        if (texture.TextureId == 0) return texture;
        // Work on the currently active unit without changing its selection or the surrounding shader state.
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
        }
        finally { GL.BindTexture(TextureTarget.Texture2D, previous); }
        return texture;
    }
    #endregion
}
