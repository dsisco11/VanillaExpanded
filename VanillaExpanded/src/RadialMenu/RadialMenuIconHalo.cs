using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.RadialMenu;

/// <summary>Owns an unscaled icon capture and composites its pixel-distance halo.</summary>
internal sealed class RadialMenuIconHalo : IDisposable
{
    #region Resources
    private readonly ICoreClientAPI capi;
    private ShaderProgram? shader;
    private MeshRef? quad;
    private int framebuffer, texture, depth;
    private int width, height;
    private readonly int[] viewport = new int[4];
    #endregion

    #region Public API
    /// <summary>Creates the owner without allocating framebuffer storage until first use.</summary>
    public RadialMenuIconHalo(ICoreClientAPI capi) => this.capi = capi;

    /// <summary>Recompiles the halo program when the menu's shaders reload.</summary>
    public bool ReloadShader()
    {
        shader?.Dispose();
        shader = null;
        var program = new ShaderProgram
        {
            VertexShader = (Shader)capi.Shader.NewShader(EnumShaderType.VertexShader),
            FragmentShader = (Shader)capi.Shader.NewShader(EnumShaderType.FragmentShader),
            AssetDomain = Constants.ModId
        };
        try
        {
            capi.Shader.RegisterFileShaderProgram("radial_menu_icon_halo", program);
            if (!program.Compile()) { program.Dispose(); return false; }
            shader = program;
            return true;
        }
        catch
        {
            program.Dispose();
            throw;
        }
    }

    /// <summary>Captures the original icon with the same projection and pixel density as the destination.</summary>
    public void Capture(IRadialMenuIcon icon, double x, double y, float size, bool enabled)
    {
        GL.GetInteger(GetPName.Viewport, viewport);
        int oldDraw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int oldRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        bool stencil = GL.IsEnabled(EnableCap.StencilTest);
        bool scissor = GL.IsEnabled(EnableCap.ScissorTest);
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        float[] clear = new float[4];
        GL.GetFloat(GetPName.ColorClearValue, clear);
        double clearDepth = GL.GetDouble(GetPName.DepthClearValue);
        try
        {
            EnsureTarget(viewport[2], viewport[3]);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.Viewport(0, 0, width, height);
            // Capture the entire silhouette before wedge clipping, including its alpha cutouts.
            GL.Disable(EnableCap.StencilTest);
            GL.Disable(EnableCap.ScissorTest);
            GL.Enable(EnableCap.DepthTest);
            GL.ColorMask(true, true, true, true);
            GL.DepthMask(true);
            GL.ClearColor(0, 0, 0, 0);
            GL.ClearDepth(1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            icon.Render(capi, x, y, size, enabled);
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, oldDraw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, oldRead);
            GL.Viewport(viewport[0], viewport[1], viewport[2], viewport[3]);
            GL.ClearColor(clear[0], clear[1], clear[2], clear[3]);
            GL.ClearDepth(clearDepth);
            GL.DepthMask(depthWrite);
            if (stencil) GL.Enable(EnableCap.StencilTest); else GL.Disable(EnableCap.StencilTest);
            if (scissor) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
            if (depthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        }
    }

    /// <summary>Draws the captured icon and its hard-edged halo through the caller's wedge stencil.</summary>
    public void Render()
    {
        if (shader is null) return;
        if (quad is null)
        {
            var data = new MeshData(4, 6, withRgba: false, withFlags: false);
            data.AddVertex(-1, -1, 0, 0, 0);
            data.AddVertex(1, -1, 0, 1, 0);
            data.AddVertex(1, 1, 0, 1, 1);
            data.AddVertex(-1, 1, 0, 0, 1);
            foreach (int index in new[] { 0, 1, 2, 0, 2, 3 }) data.AddIndex(index);
            quad = capi.Render.UploadMesh(data);
        }
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool cull = GL.IsEnabled(EnableCap.CullFace);
        bool depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        capi.Render.CurrentActiveShader?.Stop();
        try
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);
            shader.Use();
            shader.BindTexture2D("iconMask", texture, 0);
            shader.Uniform("viewportOrigin", (float)viewport[0], (float)viewport[1]);
            shader.Uniform("haloRadius", RadialMenuWedgeStyle.IconHaloRadiusPixels);
            shader.Uniform("haloTint", RadialMenuWedgeStyle.IconHaloTint);
            capi.Render.RenderMesh(quad);
        }
        finally
        {
            shader.Stop();
            GL.DepthMask(depthWrite);
            if (depthTest) GL.Enable(EnableCap.DepthTest);
            if (cull) GL.Enable(EnableCap.CullFace);
        }
    }

    /// <summary>Releases all menu-owned GPU resources.</summary>
    public void Dispose()
    {
        shader?.Dispose();
        shader = null;
        if (quad is not null) capi.Render.DeleteMesh(quad);
        quad = null;
        DeleteTarget();
    }
    #endregion

    #region Framebuffer storage
    /// <summary>Reuses viewport-sized storage, reallocating only when the destination size changes.</summary>
    private void EnsureTarget(int targetWidth, int targetHeight)
    {
        if (framebuffer != 0 && width == targetWidth && height == targetHeight) return;
        DeleteTarget();
        int oldTexture = GL.GetInteger(GetPName.TextureBinding2D);
        int oldRenderbuffer = GL.GetInteger(GetPName.RenderbufferBinding);
        try
        {
            width = targetWidth;
            height = targetHeight;
            texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            depth = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24, width, height);
            framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Could not create the radial menu icon mask framebuffer.");
        }
        catch
        {
            DeleteTarget();
            throw;
        }
        finally
        {
            GL.BindTexture(TextureTarget.Texture2D, oldTexture);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, oldRenderbuffer);
        }
    }

    /// <summary>Deletes capture attachments before reallocating or disposing them.</summary>
    private void DeleteTarget()
    {
        if (framebuffer != 0) GL.DeleteFramebuffer(framebuffer);
        if (texture != 0) GL.DeleteTexture(texture);
        if (depth != 0) GL.DeleteRenderbuffer(depth);
        framebuffer = texture = depth = 0;
    }
    #endregion
}
