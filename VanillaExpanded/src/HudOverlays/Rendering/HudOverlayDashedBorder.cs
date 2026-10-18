using System;
using System.Drawing;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns a reloadable dashed-frame shader and quad, preserving the enclosing GUI render state.</summary>
internal sealed class HudOverlayDashedBorder : IHudOverlayDashedBorder
{
    private readonly ICoreClientAPI api;
    private readonly Matrixf matrix = new();
    private readonly Vec2f size = new();
    private HudOverlayDashedBorderShaderProgram? shader;
    private MeshRef? quad;
    private bool subscribed, disposed;

    #region Public API
    /// <summary>Borrows the client API without allocating graphics resources.</summary>
    public HudOverlayDashedBorder(ICoreClientAPI api) => this.api = api;

    /// <summary>Draws all dashes with one procedural quad and restores the prior shader and GL state.</summary>
    public void Draw(RectangleF bounds, float opacity)
    {
        if (disposed || bounds.Width <= 0 || bounds.Height <= 0) return;
        Prepare();
        var renderer = api.Render;
        var previous = renderer.CurrentActiveShader;
        bool depth = GL.IsEnabled(EnableCap.DepthTest), cull = GL.IsEnabled(EnableCap.CullFace);
        bool blend = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        int sourceRgb = GL.GetInteger(GetPName.BlendSrcRgb), targetRgb = GL.GetInteger(GetPName.BlendDstRgb);
        int sourceAlpha = GL.GetInteger(GetPName.BlendSrcAlpha), targetAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        int rgbEquation = GL.GetInteger(GetPName.BlendEquationRgb), alphaEquation = GL.GetInteger(GetPName.BlendEquationAlpha);
        previous?.Stop();
        try
        {
            renderer.GLDisableDepthTest();
            GL.Disable(EnableCap.CullFace);
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            shader!.Use();
            shader.ProjectionMatrix = renderer.CurrentProjectionMatrix;
            matrix.Set(renderer.CurrentModelviewMatrix).Translate(bounds.X, bounds.Y, 1)
                .Scale(bounds.Width, bounds.Height, 1).Translate(.5f, .5f, 0).Scale(.5f, .5f, 1);
            shader.ModelViewMatrix = matrix.Values;
            size.X = bounds.Width;
            size.Y = bounds.Height;
            shader.FrameSize = size;
            shader.Opacity = opacity;
            renderer.RenderMesh(quad!);
        }
        finally
        {
            try { shader!.Stop(); previous?.Use(); }
            finally
            {
                GL.BlendFuncSeparate(0, (BlendingFactorSrc)sourceRgb, (BlendingFactorDest)targetRgb,
                    (BlendingFactorSrc)sourceAlpha, (BlendingFactorDest)targetAlpha);
                GL.BlendEquationSeparate(0, (BlendEquationMode)rgbEquation, (BlendEquationMode)alphaEquation);
                if (blend) GL.Enable(IndexedEnableCap.Blend, 0); else GL.Disable(IndexedEnableCap.Blend, 0);
                if (depth) renderer.GLEnableDepthTest(); else renderer.GLDisableDepthTest();
                if (cull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            }
        }
    }

    /// <summary>Unsubscribes shader reloads and releases both owned GPU resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed) api.Event.ReloadShader -= ReloadShader;
        subscribed = false;
        try { shader?.Dispose(); }
        finally { shader = null; quad?.Dispose(); quad = null; }
    }
    #endregion

    #region Private
    /// <summary>Initializes GPU resources on first use and listens for native shader reloads.</summary>
    private void Prepare()
    {
        if (!subscribed)
        {
            api.Event.ReloadShader += ReloadShader;
            subscribed = true;
        }
        quad ??= api.Render.UploadMesh(QuadMeshUtil.GetQuad());
        if (shader == null && !ReloadShader()) throw new InvalidOperationException("Could not compile HUD dashed border shader.");
    }

    /// <summary>Publishes a replacement only after successful compilation, retaining the previous program on failure.</summary>
    private bool ReloadShader()
    {
        var replacement = new HudOverlayDashedBorderShaderProgram(api);
        try
        {
            api.Shader.RegisterFileShaderProgram("hud_dashed_border", replacement);
            if (!replacement.Compile()) { replacement.Dispose(); return false; }
        }
        catch { replacement.Dispose(); throw; }
        shader?.Dispose();
        shader = replacement;
        return true;
    }
    #endregion
}
