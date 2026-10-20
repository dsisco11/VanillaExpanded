using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Draws a shared comparison pass using one active criteria set; GUI eligibility and scissoring belong to the caller.</summary>
internal sealed class GpuHabitatOverlay : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private IShaderProgram? shader;
    private MeshRef? quad;
    private (float Minimum, float Maximum, int SeaLevel, int WorldHeight)? heightCriteria;
    private HabitatHeightRange heightRange;
    private GpuRegionalTextures? boundRegion;
    private readonly float[] modelViewSnapshot = new float[16];

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
            GpuHabitatSamplers.Register(replacement);
        }
        catch { replacement.Dispose(); throw; }
        shader?.Dispose(); shader = replacement;
        return true;
    }

    /// <summary>Establishes common state and uniforms once after all source uploads have finished.</summary>
    internal IDisposable Begin(EcosystemStorageIdentity world, HabitatRanges ranges, float[] projection)
    {
        CheckThread();
        if (shader == null) throw new InvalidOperationException("Habitat shader is unavailable.");
        var state = new GpuHabitatPassState(api, shader);
        try
        {
            api.Render.CurrentActiveShader?.Stop();
            EnsureQuad();
            // Native map terrain uses Z=50 and markers Z=60; participate in GUI depth ordering between them.
            GL.Enable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace); GL.DepthMask(true);
            GL.Enable(IndexedEnableCap.Blend, 0);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            shader.Use();
            // Use owned linear regional and nearest terrain filtering; inherited samplers must not override those settings.
            for (int i = 0; i < GpuHabitatSamplers.Count; i++) GL.BindSampler(i, 0);
            boundRegion = null;
            ranges.Apply(shader);
            var criteria = (ranges.MinElevation, ranges.MaxElevation, world.SeaLevel, world.WorldHeight);
            if (heightCriteria != criteria)
            {
                heightRange = HabitatHeightRange.From(criteria.MinElevation, criteria.MaxElevation, criteria.SeaLevel, criteria.WorldHeight);
                heightCriteria = criteria;
            }
            shader.Uniform("surfaceHeightRange", (float)heightRange.Minimum, heightRange.Maximum);
            shader.UniformMatrix("projectionMatrix", projection);
            Array.Copy(api.Render.CurrentModelviewMatrix, modelViewSnapshot, modelViewSnapshot.Length);
            shader.UniformMatrix("modelViewMatrix", modelViewSnapshot);
            shader.Uniform("worldSize", (float)world.WorldWidth, world.WorldDepth);
            shader.Uniform("chunkSize", (float)world.ChunkSize);
            shader.Uniform("regionSize", (float)world.RegionSize);
            shader.Uniform("seaLevel", (float)world.SeaLevel);
            shader.Uniform("worldHeight", (float)world.WorldHeight);
            shader.Uniform("temperatureScale", Climate.TemperatureScaleConversion);
            shader.Uniform("worldgenTemperatureDistance", (int)(world.SeaLevel * 0.09));
            shader.Uniform("runtimeReferenceHeight", (int)(world.SeaLevel * 1.09));
            shader.Uniform("suitableColor", 0.25f, 0.8f, 0.35f, 0.3f);
            shader.Uniform("unavailableColor", 0.9f, 0.1f, 0.1f, 0.55f);
            return state;
        }
        catch { state.Dispose(); throw; }
    }

    /// <summary>Updates only changing regional bindings and tile inputs within the active pass.</summary>
    internal void Draw(GpuHabitatBundle bundle, GpuRegionalTextures region, GpuTileTextures tile,
        float x, float y, float width, float height)
    {
        CheckThread();
        if (!ReferenceEquals(boundRegion, region))
        {
            region.Bind(shader!);
            boundRegion = region;
        }
        tile.Bind(shader!);
        shader!.Uniform("screenBounds", x, y, width, height);
        shader.Uniform("tileOrigin", (float)((long)bundle.Key.X * bundle.World.ChunkSize), (float)((long)bundle.Key.Z * bundle.World.ChunkSize));
        api.Render.RenderMesh(quad!);
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
