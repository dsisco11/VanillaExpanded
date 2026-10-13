using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Owns the rotating disk shader and quad, preserving the enclosing GUI render state.</summary>
internal sealed class ApproximateTimeDialRenderer : IApproximateTimeDialRenderer
{
    private readonly ICoreClientAPI api;
    private readonly Matrixf matrix = new();
    private IShaderProgram? shader;
    private MeshRef? quad;
    private bool subscribed;

    #region Public API
    /// <summary>Prepares resources outside drawing and releases partial allocations if preparation fails.</summary>
    public static IApproximateTimeDialRenderer Create(HudOverlayPreparationContext preparation)
    {
        var result = new ApproximateTimeDialRenderer(preparation.Api);
        try
        {
            result.api.Event.ReloadShader += result.ReloadShader;
            result.subscribed = true;
            result.quad = result.api.Render.UploadMesh(QuadMeshUtil.GetQuad());
            if (!result.ReloadShader()) throw new InvalidOperationException("Could not compile environmental time dial shader.");
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    /// <summary>Draws the clipped rotating disk and restores shader, texture, blend, depth, and cull state.</summary>
    public void Draw(IRenderAPI renderer, int maskTexture, RectangleF bounds, float opacity, float rotation)
    {
        if (shader == null || quad == null) return;
        var previous = renderer.CurrentActiveShader;
        bool depth = GL.IsEnabled(EnableCap.DepthTest), cull = GL.IsEnabled(EnableCap.CullFace);
        bool blend = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        int sourceRgb = GL.GetInteger(GetPName.BlendSrcRgb), targetRgb = GL.GetInteger(GetPName.BlendDstRgb);
        int sourceAlpha = GL.GetInteger(GetPName.BlendSrcAlpha), targetAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        int rgbEquation = GL.GetInteger(GetPName.BlendEquationRgb), alphaEquation = GL.GetInteger(GetPName.BlendEquationAlpha);
        int activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        int previousTexture = GL.GetInteger(GetPName.TextureBinding2D);
        GL.ActiveTexture((TextureUnit)activeTexture);
        previous?.Stop();
        try
        {
            renderer.GLDisableDepthTest();
            GL.Disable(EnableCap.CullFace);
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            shader.Use();
            shader.BindTexture2D("shapeMask", maskTexture, 0);
            shader.UniformMatrix("projectionMatrix", renderer.CurrentProjectionMatrix);
            // Use the host GUI matrices and scissor; only this quad's local transform is changed.
            matrix.Set(renderer.CurrentModelviewMatrix).Translate(bounds.X, bounds.Y, 50)
                .Scale(bounds.Width, bounds.Height, 1).Translate(.5f, .5f, 0).Scale(.5f, .5f, 1);
            shader.UniformMatrix("modelViewMatrix", matrix.Values);
            shader.Uniform("opacity", opacity);
            shader.Uniform("rotation", rotation);
            renderer.RenderMesh(quad);
        }
        finally
        {
            try { shader.Stop(); previous?.Use(); }
            finally
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, previousTexture);
                GL.ActiveTexture((TextureUnit)activeTexture);
                GL.BlendFuncSeparate(0, (BlendingFactorSrc)sourceRgb, (BlendingFactorDest)targetRgb,
                    (BlendingFactorSrc)sourceAlpha, (BlendingFactorDest)targetAlpha);
                GL.BlendEquationSeparate(0, (BlendEquationMode)rgbEquation, (BlendEquationMode)alphaEquation);
                if (blend) GL.Enable(IndexedEnableCap.Blend, 0); else GL.Disable(IndexedEnableCap.Blend, 0);
                if (depth) renderer.GLEnableDepthTest(); else renderer.GLDisableDepthTest();
                if (cull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            }
        }
    }

    /// <summary>Unsubscribes from reloads and releases only owned GPU resources.</summary>
    public void Dispose()
    {
        if (subscribed) api.Event.ReloadShader -= ReloadShader;
        subscribed = false;
        try { shader?.Dispose(); }
        finally { shader = null; quad?.Dispose(); quad = null; }
    }
    #endregion

    #region Private
    /// <summary>Borrows the current client API until the presentation releases this renderer.</summary>
    private ApproximateTimeDialRenderer(ICoreClientAPI api) => this.api = api;

    /// <summary>Compiles a replacement before releasing the prior program, retaining it on compilation failure.</summary>
    private bool ReloadShader()
    {
        var replacement = new ApproximateTimeDialShaderProgram(api);
        try
        {
            api.Shader.RegisterFileShaderProgram("approximate_time_dial", replacement);
            if (!replacement.Compile()) { replacement.Dispose(); return false; }
        }
        catch { replacement.Dispose(); throw; }
        shader?.Dispose();
        shader = replacement;
        return true;
    }
    #endregion
}
