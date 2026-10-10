using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.AnimalSexIndicators;

/// <summary>Preserves the scene state touched by the dedicated shader activation and the indicator draw.</summary>
internal sealed class AnimalSexIndicatorRenderState : IDisposable
{
    private readonly IRenderAPI render;
    private readonly IShaderProgram? previousShader;
    private readonly int activeTexture;
    private readonly int[] textureUnits = [0];
    private readonly int[] textures = new int[1];
    private readonly int[] blend = new int[6];
    private readonly bool blendEnabled, depthWrite, cullEnabled, depthEnabled;
    private readonly int depthFunction;
    private readonly bool[][] auxiliaryColorMasks = [new bool[4], new bool[4], new bool[4]];

    #region Public API
    /// <summary>Captures exact indexed blend factors and bindings before activating the icon shader.</summary>
    public AnimalSexIndicatorRenderState(IRenderAPI render)
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
        depthEnabled = GL.IsEnabled(EnableCap.DepthTest);
        depthFunction = GL.GetInteger(GetPName.DepthFunc);
        cullEnabled = GL.IsEnabled(EnableCap.CullFace);
        for (int i = 0; i < auxiliaryColorMasks.Length; i++)
            GL.GetBoolean((GetIndexedPName)GetPName.ColorWritemask, i + 1, auxiliaryColorMasks[i]);
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
            for (int i = 0; i < auxiliaryColorMasks.Length; i++)
            {
                bool[] mask = auxiliaryColorMasks[i];
                GL.ColorMask(i + 1, mask[0], mask[1], mask[2], mask[3]);
            }
            GL.DepthMask(depthWrite);
            GL.DepthFunc((DepthFunction)depthFunction);
            if (depthEnabled) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (cullEnabled) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        }
    }
    #endregion
}
