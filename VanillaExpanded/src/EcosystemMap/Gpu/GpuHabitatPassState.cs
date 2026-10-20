using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Captures and restores caller graphics state once around a complete ecosystem pass.</summary>
internal sealed class GpuHabitatPassState : IDisposable
{
    private readonly IShaderProgram shader;
    private readonly IShaderProgram? previous;
    private readonly int program, vertexArray, arrayBuffer, srcRgb, dstRgb, srcAlpha, dstAlpha, rgbEquation, alphaEquation, activeUnit;
    private readonly bool depth, cull, blend, writeDepth;
    private readonly int[] bindings = new int[GpuHabitatSamplers.Count], samplers = new int[GpuHabitatSamplers.Count];
    private bool disposed;

    #region Public API
    /// <summary>Snapshots only state touched by the pass, retaining the caller's active texture unit.</summary>
    internal GpuHabitatPassState(ICoreClientAPI api, IShaderProgram shader)
    {
        this.shader = shader;
        program = GL.GetInteger(GetPName.CurrentProgram);
        vertexArray = GL.GetInteger(GetPName.VertexArrayBinding);
        arrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
        previous = api.Render.CurrentActiveShader;
        depth = GL.IsEnabled(EnableCap.DepthTest); cull = GL.IsEnabled(EnableCap.CullFace);
        blend = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        srcRgb = GL.GetInteger(GetPName.BlendSrcRgb); dstRgb = GL.GetInteger(GetPName.BlendDstRgb);
        srcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha); dstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        rgbEquation = GL.GetInteger(GetPName.BlendEquationRgb); alphaEquation = GL.GetInteger(GetPName.BlendEquationAlpha);
        writeDepth = GL.GetBoolean(GetPName.DepthWritemask);
        activeUnit = GL.GetInteger(GetPName.ActiveTexture);
        try
        {
            for (int i = 0; i < GpuHabitatSamplers.Count; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                bindings[i] = GL.GetInteger(GetPName.TextureBinding2D);
                samplers[i] = GL.GetInteger(GetPName.SamplerBinding);
            }
        }
        finally { GL.ActiveTexture((TextureUnit)activeUnit); }
    }

    /// <summary>Restores touched bindings and native shader ownership even when drawing fails.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shader.Stop();
        GL.BindVertexArray(vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, arrayBuffer);
        for (int i = 0; i < GpuHabitatSamplers.Count; i++) { GL.ActiveTexture(TextureUnit.Texture0 + i); GL.BindTexture(TextureTarget.Texture2D, bindings[i]); GL.BindSampler(i, samplers[i]); }
        GL.ActiveTexture((TextureUnit)activeUnit);
        GL.BlendFuncSeparate(0, (BlendingFactorSrc)srcRgb, (BlendingFactorDest)dstRgb, (BlendingFactorSrc)srcAlpha, (BlendingFactorDest)dstAlpha);
        GL.BlendEquationSeparate(0, (BlendEquationMode)rgbEquation, (BlendEquationMode)alphaEquation);
        if (blend) GL.Enable(IndexedEnableCap.Blend, 0); else GL.Disable(IndexedEnableCap.Blend, 0);
        GL.DepthMask(writeDepth);
        if (depth) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        if (cull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
        if (previous != null) previous.Use(); else GL.UseProgram(program);
    }
    #endregion
}
