using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.ItemRendering;

/// <summary>Restores the shared GUI program, draw bindings, and matrix-stack boundary after one prepared draw.</summary>
internal sealed class ToolHeadRenderState : IDisposable
{
    private readonly IRenderAPI render;
    private readonly IShaderProgram shader;
    private readonly ToolHeadShaderUniformState uniforms;
    private readonly int activeTexture;
    private readonly int[] textures = new int[2], samplers = new int[2];
    private readonly int[] uniformPoints, uniformBuffers;
    private readonly long[] uniformStarts, uniformSizes;
    private readonly int uniformBinding;
    private readonly (EnableCap Cap, bool Enabled)[] enables;
    private readonly int[] blend = new int[6];
    private readonly bool depthWrite;
    private readonly int cullMode, frontFace, depthFunction;
    private readonly StackMatrix4 modelView, projection;
    private readonly int modelDepth, projectionDepth;
    private readonly double[] modelTop, projectionTop;
    private bool disposed;

    #region Public API
    /// <summary>Captures caller values before preparation; the caller must already have the GUI shader active.</summary>
    internal ToolHeadRenderState(IRenderAPI render, IShaderProgram shader)
    {
        this.render = render;
        this.shader = shader;
        uniforms = ToolHeadShaderUniformState.Capture(render, shader);
        activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        for (int i = 0; i < 2; i++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + i);
            textures[i] = GL.GetInteger(GetPName.TextureBinding2D);
            samplers[i] = GL.GetInteger(GetPName.SamplerBinding);
        }
        GL.ActiveTexture((TextureUnit)activeTexture);
        GL.GetProgram(shader.ProgramId, GetProgramParameterName.ActiveUniformBlocks, out int blocks);
        uniformPoints = new int[blocks];
        uniformBuffers = new int[blocks];
        uniformStarts = new long[blocks];
        uniformSizes = new long[blocks];
        uniformBinding = GL.GetInteger(GetPName.UniformBufferBinding);
        for (int i = 0; i < blocks; i++)
        {
            GL.GetActiveUniformBlock(shader.ProgramId, i, ActiveUniformBlockParameter.UniformBlockBinding, out uniformPoints[i]);
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, uniformPoints[i], out uniformBuffers[i]);
            GL.GetInteger64((GetIndexedPName)0x8A29, uniformPoints[i], out uniformStarts[i]); // GL_UNIFORM_BUFFER_START
            GL.GetInteger64((GetIndexedPName)0x8A2A, uniformPoints[i], out uniformSizes[i]); // GL_UNIFORM_BUFFER_SIZE
        }
        enables = new[] { EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace, EnableCap.StencilTest, EnableCap.ScissorTest }
            .Select(cap => (Cap: cap, Enabled: GL.IsEnabled(cap))).ToArray();
        depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        depthFunction = GL.GetInteger(GetPName.DepthFunc);
        cullMode = GL.GetInteger(GetPName.CullFaceMode);
        frontFace = GL.GetInteger(GetPName.FrontFace);
        GetPName[] blendNames = [GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha,
            GetPName.BlendDstAlpha, GetPName.BlendEquationRgb, GetPName.BlendEquationAlpha];
        for (int i = 0; i < blend.Length; i++) blend[i] = GL.GetInteger(blendNames[i]);
        modelView = render.MvMatrix;
        projection = render.PMatrix;
        modelDepth = modelView.Count;
        projectionDepth = projection.Count;
        modelTop = (double[])modelView.Top.Clone();
        projectionTop = (double[])projection.Top.Clone();
    }

    /// <summary>Restores the exact enclosing GUI values without resetting shared uniforms to arbitrary defaults.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            // Engine switching keeps its active-program bookkeeping consistent with GL.
            if (!ReferenceEquals(render.CurrentActiveShader, shader))
            {
                render.CurrentActiveShader?.Stop();
                shader.Use();
            }
            uniforms.Dispose();
        }
        finally
        {
            for (int i = 0; i < 2; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture2D, textures[i]);
                GL.BindSampler(i, samplers[i]);
            }
            GL.ActiveTexture((TextureUnit)activeTexture);
            for (int i = 0; i < uniformPoints.Length; i++)
            {
                GL.UniformBlockBinding(shader.ProgramId, i, uniformPoints[i]);
                if (uniformBuffers[i] != 0 && uniformSizes[i] > 0)
                    GL.BindBufferRange(BufferRangeTarget.UniformBuffer, uniformPoints[i], uniformBuffers[i],
                        (IntPtr)uniformStarts[i], (IntPtr)uniformSizes[i]);
                else GL.BindBufferBase(BufferRangeTarget.UniformBuffer, uniformPoints[i], uniformBuffers[i]);
            }
            GL.BindBuffer(BufferTarget.UniformBuffer, uniformBinding);
            foreach (var flag in enables)
                if (flag.Enabled) GL.Enable(flag.Cap); else GL.Disable(flag.Cap);
            GL.DepthMask(depthWrite);
            GL.DepthFunc((DepthFunction)depthFunction);
            GL.CullFace((TriangleFace)cullMode);
            GL.FrontFace((FrontFaceDirection)frontFace);
            GL.BlendFuncSeparate((BlendingFactorSrc)blend[0], (BlendingFactorDest)blend[1],
                (BlendingFactorSrc)blend[2], (BlendingFactorDest)blend[3]);
            GL.BlendEquationSeparate((BlendEquationMode)blend[4], (BlendEquationMode)blend[5]);
            RestoreStack(modelView, modelDepth, modelTop);
            RestoreStack(projection, projectionDepth, projectionTop);
        }
    }
    #endregion

    #region Private
    /// <summary>Restores the caller's top and depth; callbacks must not modify lower caller-owned stack entries.</summary>
    private static void RestoreStack(StackMatrix4 stack, int depth, double[] top)
    {
        while (stack.Count > depth) stack.Pop();
        while (stack.Count < depth) stack.Push(top);
        Array.Copy(top, stack.Top, 16);
    }
    #endregion
}
