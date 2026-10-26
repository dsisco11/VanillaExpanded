using System;
using System.Collections.Generic;
using System.Drawing;
using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns one shared procedural backdrop program and quad for the client HUD system.</summary>
internal sealed class HudOverlayIconBackdrop : IHudOverlayIconBackdrop, IHudOverlayBackdropBatch, IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Matrixf matrix = new();
    private HudOverlayBackdropShaderProgram? shader;
    private MeshRef? quad;
    private bool subscribed;
    private readonly List<(RectangleF Circle, RectangleF Clip, float Opacity)> pending = new();
    private RectangleF? collectionClip;
    private readonly ElementBounds scissor = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    #region Public API
    #region Resources
    /// <summary>Borrows the client API without allocating graphics resources during startup.</summary>
    public HudOverlayIconBackdrop(ICoreClientAPI api) => this.api = api;

    /// <summary>Obtains the runtime-owned backdrop and prepares resources outside the draw callback.</summary>
    public static IHudOverlayIconBackdrop? Load(HudOverlayPreparationContext context)
    {
        var backdrop = context.Api.ModLoader?.GetModSystem<HudOverlaySystem>()?.Backdrop;
        backdrop?.Prepare();
        return backdrop;
    }

    /// <summary>Unhooks reload notifications and releases owned graphics resources.</summary>
    public void Dispose()
    {
        Clear();
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
    #region Frame collection and rendering

    /// <summary>Queues one circle during the host's clipped collection pass; performs no GL calls.</summary>
    public void Queue(IRenderAPI renderer, double x, double y, double scale)
    {
        if (!collectionClip.HasValue) return;
        var circle = new RectangleF((float)x, (float)y, (float)(HudOverlaySizes.Standard * scale), (float)(HudOverlaySizes.Standard * scale));
        var clip = RectangleF.Intersect(circle, collectionClip.Value);
        if (clip.Width > 0 && clip.Height > 0) pending.Add((circle, clip, DrawOpacity.Current));
    }

    /// <summary>Scopes collection to one member and rolls back its queued circles if its provider fails.</summary>
    public void Collect(RectangleF clip, Action collect)
    {
        int start = pending.Count;
        collectionClip = clip;
        try { collect(); }
        catch { pending.RemoveRange(start, pending.Count - start); throw; }
        finally { collectionClip = null; }
    }

    /// <summary>Discards per-frame positions without touching owned graphics resources.</summary>
    public void Clear() { pending.Clear(); collectionClip = null; }

    /// <summary>Draws every queued circle with one state capture and program activation, then restores the GUI.</summary>
    public void Flush(IRenderAPI renderer)
    {
        if (pending.Count == 0) return;
        try { DrawBatch(renderer); }
        finally { Clear(); }
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Preserves the enclosing state once; per-circle work changes only the model matrix and scissor.</summary>
    private void DrawBatch(IRenderAPI renderer)
    {
        if (shader == null || quad == null) return;
        var previous = renderer.CurrentActiveShader;
        bool cull = GL.IsEnabled(EnableCap.CullFace);
        bool depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        bool blend = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        int sourceRgb = GL.GetInteger(GetPName.BlendSrcRgb), targetRgb = GL.GetInteger(GetPName.BlendDstRgb);
        int sourceAlpha = GL.GetInteger(GetPName.BlendSrcAlpha), targetAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        int rgbEquation = GL.GetInteger(GetPName.BlendEquationRgb), alphaEquation = GL.GetInteger(GetPName.BlendEquationAlpha);
        previous?.Stop();
        try
        {
            // Soft circles obey native GUI depth without writing invisible square occluders.
            renderer.GLDepthMask(false);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            shader.Use();
            shader.ProjectionMatrix = renderer.CurrentProjectionMatrix;
            var modelView = renderer.CurrentModelviewMatrix;
            foreach (var item in pending)
            {
                matrix.Set(modelView).Translate(item.Circle.X, item.Circle.Y, 49f)
                    .Scale(item.Circle.Width, item.Circle.Height, 1).Translate(.5f, .5f, 0).Scale(.5f, .5f, 1);
                shader.ModelViewMatrix = matrix.Values;
                shader.Opacity = item.Opacity;
                scissor.absFixedX = item.Clip.X; scissor.absFixedY = item.Clip.Y;
                scissor.absInnerWidth = item.Clip.Width; scissor.absInnerHeight = item.Clip.Height;
                scissor.Initialized = true;
                renderer.PushScissor(scissor, true);
                try { renderer.RenderMesh(quad); }
                finally { renderer.PopScissor(); }
            }
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
                if (cull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
                renderer.GLDepthMask(depthWrite);
            }
        }
    }

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
