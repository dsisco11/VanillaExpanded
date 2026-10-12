using System;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns one shared procedural backdrop program and quad for the client HUD system.</summary>
internal sealed class HudOverlayIconBackdrop : IHudOverlayIconBackdrop, IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Matrixf matrix = new();
    private HudOverlayBackdropShaderProgram? shader;
    private MeshRef? quad;
    private bool subscribed;
    #region Public API
    /// <summary>Borrows the client API without allocating graphics resources during startup.</summary>
    public HudOverlayIconBackdrop(ICoreClientAPI api) => this.api = api;

    /// <summary>Obtains the runtime-owned backdrop and prepares resources outside the draw callback.</summary>
    public static IHudOverlayIconBackdrop? Load(HudOverlayPreparationContext context)
    {
        var backdrop = context.Api.ModLoader?.GetModSystem<HudOverlaySystem>()?.Backdrop;
        backdrop?.Prepare();
        return backdrop;
    }

    /// <summary>Draws a smooth black circle while restoring the enclosing program and depth state.</summary>
    public void Draw(IRenderAPI renderer, double x, double y, double scale)
    {
        if (shader == null || quad == null) return;
        var previous = renderer.CurrentActiveShader;
        bool depth = GL.IsEnabled(EnableCap.DepthTest);
        bool cull = GL.IsEnabled(EnableCap.CullFace);
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
            shader.Use();
            matrix.Set(renderer.CurrentModelviewMatrix).Translate(x, y, 49f)
                .Scale((float)(24 * scale), (float)(24 * scale), 1).Translate(.5f, .5f, 0).Scale(.5f, .5f, 1);
            shader.ProjectionMatrix = renderer.CurrentProjectionMatrix;
            shader.ModelViewMatrix = matrix.Values;
            renderer.RenderMesh(quad);
        }
        finally
        {
            try { shader.Stop(); previous?.Use(); }
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

    /// <summary>Unhooks reload notifications and releases owned graphics resources.</summary>
    public void Dispose()
    {
        if (subscribed) api.Event.ReloadShader -= ReloadShader;
        subscribed = false;
        try { shader?.Dispose(); }
        finally
        {
            shader = null;
            quad?.Dispose(); quad = null;
        }
    }
    #endregion
    #region Private
    /// <summary>Lazily prepares the shared quad and shader, subscribing once to native shader reloads.</summary>
    private void Prepare()
    {
        if (!subscribed)
        {
            api.Event.ReloadShader += ReloadShader;
            subscribed = true;
        }
        quad ??= api.Render.UploadMesh(QuadMeshUtil.GetQuad());
        if (shader == null && !ReloadShader()) throw new InvalidOperationException("Could not compile HUD backdrop shader.");
    }

    /// <summary>Replaces the program only after successful compilation, keeping the prior program on failure.</summary>
    private bool ReloadShader()
    {
        var replacement = new HudOverlayBackdropShaderProgram(api);
        try
        {
            api.Shader.RegisterFileShaderProgram("hud_icon_backdrop", replacement);
            if (!replacement.Compile()) { replacement.Dispose(); return false; }
        }
        catch { replacement.Dispose(); throw; }
        shader?.Dispose();
        shader = replacement;
        return true;
    }
    #endregion
}
