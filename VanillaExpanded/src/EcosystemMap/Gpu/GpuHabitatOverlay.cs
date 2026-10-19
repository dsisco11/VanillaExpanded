using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Draws one tile's comparison mask using a single active criteria set; GUI eligibility and scissoring belong to the caller.</summary>
internal sealed class GpuHabitatOverlay : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private IShaderProgram? shader;
    private MeshRef? quad;

    #region Public API
    /// <summary>Creates an owner on the graphics thread; no shader/texture allocation occurs on background input workers.</summary>
    internal GpuHabitatOverlay(ICoreClientAPI api) { this.api = api; }

    /// <summary>Replaces the engine-managed file shader only on successful compilation.</summary>
    internal bool Reload()
    {
        CheckThread();
        var replacement = api.Shader.NewShaderProgram();
        try
        {
            replacement.AssetDomain = "vanillaexpanded";
            replacement.VertexShader = api.Shader.NewShader(EnumShaderType.VertexShader);
            replacement.FragmentShader = api.Shader.NewShader(EnumShaderType.FragmentShader);
            api.Shader.RegisterFileShaderProgram("ecosystem_habitat", replacement);
            if (!replacement.Compile()) { replacement.Dispose(); return false; }
        }
        catch { replacement.Dispose(); throw; }
        shader?.Dispose(); shader = replacement;
        return true;
    }

    /// <summary>Performs GPU sampling and comparisons in the caller's framebuffer and existing map scissor.</summary>
    internal void Draw(GpuHabitatBundle bundle, GpuRegionalTextures region, GpuTileTextures tile,
        HabitatRanges ranges, float[] projection, float x, float y, float width, float height,
        bool fullMapActive)
    {
        CheckThread();
        if (!fullMapActive || shader == null) return;
        EnsureQuad();
        var previous = api.Render.CurrentActiveShader;
        bool depth = GL.IsEnabled(EnableCap.DepthTest), cull = GL.IsEnabled(EnableCap.CullFace);
        bool blend = GL.IsEnabled(IndexedEnableCap.Blend, 0);
        int srcRgb = GL.GetInteger(GetPName.BlendSrcRgb), dstRgb = GL.GetInteger(GetPName.BlendDstRgb);
        int srcAlpha = GL.GetInteger(GetPName.BlendSrcAlpha), dstAlpha = GL.GetInteger(GetPName.BlendDstAlpha);
        int rgbEquation = GL.GetInteger(GetPName.BlendEquationRgb), alphaEquation = GL.GetInteger(GetPName.BlendEquationAlpha);
        bool writeDepth = GL.GetBoolean(GetPName.DepthWritemask);
        int activeUnit = GL.GetInteger(GetPName.ActiveTexture);
        int[] bindings = new int[5];
        for (int i = 0; i < 5; i++) { GL.ActiveTexture(TextureUnit.Texture0 + i); bindings[i] = GL.GetInteger(GetPName.TextureBinding2D); }
        previous?.Stop();
        try
        {
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace); GL.DepthMask(false);
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            shader.Use();
            region.Bind(shader); tile.Bind(shader); ranges.Apply(shader);
            shader.UniformMatrix("projectionMatrix", projection);
            shader.Uniform("screenBounds", x, y, width, height);
            shader.Uniform("tileOrigin", (float)((long)bundle.Key.X * bundle.World.ChunkSize), (float)((long)bundle.Key.Z * bundle.World.ChunkSize));
            shader.Uniform("worldSize", (float)bundle.World.WorldWidth, bundle.World.WorldDepth);
            shader.Uniform("chunkSize", (float)bundle.World.ChunkSize);
            shader.Uniform("regionSize", (float)bundle.World.RegionSize);
            shader.Uniform("seaLevel", (float)bundle.World.SeaLevel);
            shader.Uniform("worldHeight", (float)bundle.World.WorldHeight);
            shader.Uniform("temperatureScale", Climate.TemperatureScaleConversion);
            shader.Uniform("worldgenTemperatureDistance", (int)(bundle.World.SeaLevel * 0.09));
            shader.Uniform("runtimeReferenceHeight", (int)(bundle.World.SeaLevel * 1.09));
            shader.Uniform("suitableColor", 0.25f, 0.8f, 0.35f, 0.3f);
            shader.Uniform("unavailableColor", 0.9f, 0.1f, 0.1f, 0.55f);
            api.Render.RenderMesh(quad!);
        }
        finally
        {
            shader.Stop();
            for (int i = 0; i < 5; i++) { GL.ActiveTexture(TextureUnit.Texture0 + i); GL.BindTexture(TextureTarget.Texture2D, bindings[i]); }
            GL.ActiveTexture((TextureUnit)activeUnit);
            GL.BlendFuncSeparate(0, (BlendingFactorSrc)srcRgb, (BlendingFactorDest)dstRgb, (BlendingFactorSrc)srcAlpha, (BlendingFactorDest)dstAlpha);
            GL.BlendEquationSeparate(0, (BlendEquationMode)rgbEquation, (BlendEquationMode)alphaEquation);
            if (blend) GL.Enable(IndexedEnableCap.Blend, 0); else GL.Disable(IndexedEnableCap.Blend, 0);
            GL.DepthMask(writeDepth);
            if (depth) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (cull) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            previous?.Use();
        }
    }

    /// <summary>Releases shader and mesh resources on the creating graphics thread.</summary>
    public void Dispose()
    {
        CheckThread(); shader?.Dispose(); shader = null;
        if (quad != null) api.Render.DeleteMesh(quad);
        quad = null;
    }
    #endregion

    #region Private
    /// <summary>Uploads reusable tile geometry once, independently of active species or environmental values.</summary>
    private void EnsureQuad()
    {
        if (quad != null) return;
        var data = new MeshData(4, 6, withRgba: false, withFlags: false);
        data.AddVertex(-1, -1, 0, 0, 0); data.AddVertex(1, -1, 0, 1, 0);
        data.AddVertex(1, 1, 0, 1, 1); data.AddVertex(-1, 1, 0, 0, 1);
        foreach (int index in new[] { 0, 1, 2, 0, 2, 3 }) data.AddIndex(index);
        quad = api.Render.UploadMesh(data);
    }

    /// <summary>Prevents accidental GL use on background storage/input jobs.</summary>
    private void CheckThread()
    {
        if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Habitat rendering requires its graphics thread.");
    }
    #endregion
}
