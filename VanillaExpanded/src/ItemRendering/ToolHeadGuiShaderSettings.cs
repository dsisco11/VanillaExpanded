using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ItemRendering;

/// <summary>Applies the ordinary engine GUI mesh effects around the dedicated model matrix.</summary>
internal static class ToolHeadGuiShaderSettings
{
    #region Public API
    /// <summary>Configures prepared geometry and effects without rebuilding inventory presentation.</summary>
    internal static bool Apply(ICoreClientAPI api, IShaderProgram shader, ItemStack stack, ItemRenderInfo info, float[] model)
    {
        int temperature = (int)stack.Collectible.GetTemperature(api.World, stack);
        int glow = GameMath.Clamp((temperature - 550) / 2, 0, 255);
        bool temperatureMode = stack.Attributes.HasAttribute("temperature");
        float[] incandescent = ColorUtil.GetIncandescenceColorAsColor4f(temperature);
        shader.Uniform("normalShaded", info.NormalShaded ? 1 : 0);
        // Normals already include radial rotation. Keep a normalized light toward screen-right, screen-up and the viewer.
        shader.Uniform("lightPosition", 0.5773503f, -0.5773503f, -0.5773503f);
        shader.Uniform("rgbaIn", new Vec4f(1, 1, 1, 1));
        shader.Uniform("applyColor", info.ApplyColor ? 1 : 0);
        shader.Uniform("alphaTest", info.AlphaTest);
        shader.Uniform("extraGlow", glow);
        shader.Uniform("tempGlowMode", temperatureMode ? 1 : 0);
        shader.Uniform("rgbaGlowIn", new Vec4f(temperatureMode ? incandescent[0] : 1,
            temperatureMode ? incandescent[1] : 1, temperatureMode ? incandescent[2] : 1, glow / 255f));
        shader.Uniform("damageEffect", info.DamageEffect);
        shader.Uniform("overlayOpacity", info.OverlayTexture is not null ? info.OverlayOpacity : 0);
        if (info.OverlayTexture is { } overlay && info.OverlayOpacity > 0)
        {
            shader.BindTexture2D("tex2dOverlay", overlay.TextureId, 1);
            shader.Uniform("overlayTextureSize", (float)overlay.Width, (float)overlay.Height);
            shader.Uniform("baseTextureSize", (float)info.TextureSize.Width, (float)info.TextureSize.Height);
            var origin = api.Render.GetTextureAtlasPosition(stack);
            shader.Uniform("baseUvOrigin", origin.x1, origin.y1);
        }
        // Own matrices avoid shared engine scratch aliases and remain valid through nested GUI calls.
        float[] view = new Matrixf().Set(model).ReverseMul(api.Render.CurrentModelviewMatrix).Values;
        foreach (float component in view)
            if (!float.IsFinite(component)) return false;
        shader.UniformMatrix("modelMatrix", model);
        shader.UniformMatrix("projectionMatrix", (float[])api.Render.CurrentProjectionMatrix.Clone());
        shader.UniformMatrix("modelViewMatrix", view);
        shader.Uniform("applyModelMat", 1);
        shader.Uniform("noTexture", 0f);
        shader.Uniform("darkEdges", 0);
        shader.Uniform("transparentCenter", 0);
        shader.Uniform("sepiaLevel", 0f);
        return true;
    }
    #endregion
}
