using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Restores the engine framebuffer bookkeeping and GL bindings around clock background preparation.</summary>
internal sealed class ApproximateTimeFaceRenderState : IDisposable
{
    private readonly IRenderAPI render;
    private readonly FrameBufferRef framebuffer;
    private readonly IShaderProgram? shader;
    private readonly int program, drawFramebuffer, readFramebuffer, activeTexture, vertexArray, arrayBuffer, unpackBuffer;
    private readonly int[] viewport = new int[4], textures = new int[3], samplers = new int[3], polygonModes = new int[2];
    private readonly bool[] colorMask = new bool[4];
    private readonly EnableCap[] caps = [EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace,
        EnableCap.StencilTest, EnableCap.ScissorTest, EnableCap.FramebufferSrgb, EnableCap.RasterizerDiscard, EnableCap.ColorLogicOp];
    private readonly bool[] enabled;

    #region Public API
    /// <summary>Captures values before engine texture loading, mesh upload, or framebuffer allocation can change them.</summary>
    public ApproximateTimeFaceRenderState(IRenderAPI render)
    {
        this.render = render;
        framebuffer = render.CurrentFrameBuffer;
        shader = render.CurrentActiveShader;
        program = GL.GetInteger(GetPName.CurrentProgram);
        drawFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        readFramebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
        activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        vertexArray = GL.GetInteger(GetPName.VertexArrayBinding);
        arrayBuffer = GL.GetInteger(GetPName.ArrayBufferBinding);
        unpackBuffer = GL.GetInteger(GetPName.PixelUnpackBufferBinding);
        GL.GetInteger(GetPName.Viewport, viewport);
        GL.GetInteger(GetPName.PolygonMode, polygonModes);
        GL.GetBoolean(GetPName.ColorWritemask, colorMask);
        enabled = new bool[caps.Length];
        for (int index = 0; index < caps.Length; index++) enabled[index] = GL.IsEnabled(caps[index]);
        for (int unit = 0; unit < textures.Length; unit++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            textures[unit] = GL.GetInteger(GetPName.TextureBinding2D);
            samplers[unit] = GL.GetInteger(GetPName.SamplerBinding);
        }
        GL.ActiveTexture((TextureUnit)activeTexture);
    }

    /// <summary>Configures a complete linear-color overwrite without inheriting the HUD's clipping or blend state.</summary>
    public void ConfigureComposition()
    {
        foreach (var cap in caps) GL.Disable(cap);
        GL.ColorMask(true, true, true, true);
        GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
        for (int unit = 0; unit < samplers.Length; unit++) GL.BindSampler(unit, 0);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Restores both the engine's active objects and the exact caller framebuffer, viewport, and draw bindings.</summary>
    public void Dispose()
    {
        // Engine switching keeps its active-shader and framebuffer references aligned with GL.
        if (!ReferenceEquals(render.CurrentActiveShader, shader)) render.CurrentActiveShader?.Stop();
        if (shader != null) shader.Use();
        else GL.UseProgram(program);
        render.CurrentFrameBuffer = framebuffer;
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
        GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
        GL.BindVertexArray(vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, arrayBuffer);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, unpackBuffer);
        for (int unit = 0; unit < textures.Length; unit++)
        {
            GL.ActiveTexture(TextureUnit.Texture0 + unit);
            GL.BindTexture(TextureTarget.Texture2D, textures[unit]);
            GL.BindSampler(unit, samplers[unit]);
        }
        GL.ActiveTexture((TextureUnit)activeTexture);
        GL.ColorMask(colorMask[0], colorMask[1], colorMask[2], colorMask[3]);
        // Core-profile callers use a shared front/back polygon mode.
        GL.PolygonMode(TriangleFace.FrontAndBack, (PolygonMode)polygonModes[0]);
        for (int index = 0; index < caps.Length; index++)
            if (enabled[index]) GL.Enable(caps[index]); else GL.Disable(caps[index]);
    }
    #endregion
}
