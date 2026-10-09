using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.SpawnDecal;

/// <summary>Preserves the scene state touched by standard-shader preparation and the decal draw.</summary>
internal sealed class SpawnDecalRenderState : IDisposable
{
    private readonly IRenderAPI render;
    private readonly IShaderProgram? previousShader;
    private readonly int activeTexture;
    private readonly int[] textureUnits = [0, 2, 3, 4];
    private readonly int[] textures = new int[4];
    private readonly int[] blend = new int[6];
    private readonly bool blendEnabled, depthWrite, cullEnabled;

    #region Public API
    /// <summary>Captures exact indexed blend factors and bindings before the engine prepares the standard shader.</summary>
    public SpawnDecalRenderState(IRenderAPI render)
    {
        this.render = render;
        previousShader = render.CurrentActiveShader;
        activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        for (int i = 0; i < textureUnits.Length; i++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + textureUnits[i]);
            textures[i] = GL.GetInteger(GetPName.TextureBinding2D);
        }
        GL.ActiveTexture((TextureUnit)activeTexture);
        GetPName[] names = [GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha,
            GetPName.BlendDstAlpha, GetPName.BlendEquationRgb, GetPName.BlendEquationAlpha];
        for (int i = 0; i < names.Length; i++)
            GL.GetInteger((GetIndexedPName)names[i], 0, out blend[i]);
        blendEnabled = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        cullEnabled = GL.IsEnabled(EnableCap.CullFace);
        previousShader?.Stop();
    }

    /// <summary>Restores the enclosing engine program and only the GL state changed by this draw.</summary>
    public void Dispose()
    {
        // Use the engine handoff so its current-program tracking stays consistent with OpenGL.
        try
        {
            render.CurrentActiveShader?.Stop();
            previousShader?.Use();
        }
        finally
        {
            // Program activation can bind shadow maps, so restore texture bindings after the handoff.
            for (int i = 0; i < textureUnits.Length; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + textureUnits[i]);
                GL.BindTexture(TextureTarget.Texture2D, textures[i]);
            }
            GL.ActiveTexture((TextureUnit)activeTexture);
            GL.BlendEquationSeparate(0, (BlendEquationMode)blend[4], (BlendEquationMode)blend[5]);
            GL.BlendFuncSeparate(0, (BlendingFactorSrc)blend[0], (BlendingFactorDest)blend[1],
                (BlendingFactorSrc)blend[2], (BlendingFactorDest)blend[3]);
            if (blendEnabled) GL.Enable(IndexedEnableCap.Blend, 0); else GL.Disable(IndexedEnableCap.Blend, 0);
            GL.DepthMask(depthWrite);
            if (cullEnabled) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        }
    }
    #endregion
}
