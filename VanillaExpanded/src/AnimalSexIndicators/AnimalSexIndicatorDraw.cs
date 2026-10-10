using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.AnimalSexIndicators;

/// <summary>Updates per-animal inputs and draws through the caller's active icon shader.</summary>
internal static class AnimalSexIndicatorDraw
{
    #region Public API
    /// <summary>Draws one symbol without changing shader activation or frame-wide camera uniforms.</summary>
    public static void Draw(IRenderAPI render, IShaderProgram shader, MeshRef quad, int textureId, float[] model, Vec4f tint)
    {
        // The renderer keeps this program active across all entities; only instance inputs vary here.
        shader.BindTexture2D("iconTexture", textureId, 0);
        shader.UniformMatrix("modelMatrix", model);
        shader.Uniform("iconTint", tint);
        render.RenderMesh(quad);
    }
    #endregion
}
