using OpenTK.Graphics.OpenGL4;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Preserves depth and culling while using the engine slot-grid blend contract.</summary>
internal sealed class ItemSlotIndicatorDrawState(IRenderAPI render)
{
    private bool depth, depthWrite, cull;

    #region Public API
    /// <summary>Captures only state whose inherited value is needed after the indicator draw.</summary>
    internal void Capture()
    {
        depth = GL.IsEnabled(EnableCap.DepthTest);
        depthWrite = GL.GetInteger(GetPName.DepthWritemask) != 0;
        cull = GL.IsEnabled(EnableCap.CullFace);
    }

    /// <summary>Prevents indicator depth writes and applies engine premultiplied blending.</summary>
    internal void Apply()
    {
        render.GLEnableDepthTest();
        render.GLDepthMask(false);
        render.GlDisableCullFace();
        render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
    }

    /// <summary>Restores depth/culling and the standard blend mode left by the slot background helper.</summary>
    internal void Restore()
    {
        // Slot backgrounds and highlights leave Standard blending before the item-render hook.
        // RenderMesh owns VAO/EBO binding; subsequent engine draws bind their own meshes and textures.
        render.GlToggleBlend(true, EnumBlendMode.Standard);
        render.GLDepthMask(depthWrite);
        if (depth) render.GLEnableDepthTest(); else render.GLDisableDepthTest();
        if (cull) GL.Enable(EnableCap.CullFace); else render.GlDisableCullFace();
    }
    #endregion
}
