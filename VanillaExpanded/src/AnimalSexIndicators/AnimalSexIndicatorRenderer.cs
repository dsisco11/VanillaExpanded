using System;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.AnimalSexIndicators;

/// <summary>Draws shared camera-facing sex symbols against the world's existing depth buffer.</summary>
internal sealed class AnimalSexIndicatorRenderer : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly MeshRef quad;
    private readonly int maleTextureId;
    private readonly int femaleTextureId;
    private readonly Matrixf model = new();
    private readonly Vec4f tint = new();
    private AnimalSexIndicatorShaderProgram? shader;
    private bool disposed;

    #region Public API
    /// <summary>Draws late in the opaque world pass, after terrain has populated depth.</summary>
    public double RenderOrder => 0.99;
    /// <summary>Declares the maximum supported viewing distance.</summary>
    public int RenderRange => 64;

    /// <summary>Loads cached symbol assets and uploads one quad, then registers world rendering.</summary>
    public AnimalSexIndicatorRenderer(ICoreClientAPI api)
    {
        this.api = api;
        var data = QuadMeshUtil.GetCustomQuadModelData(-0.5f, -0.5f, 0, 1, 1);
        // Pixel rows start at the top, while world-space quad coordinates grow upward.
        for (int i = 1; i < data.Uv.Length; i += 2) data.Uv[i] = 1 - data.Uv[i];
        quad = api.Render.UploadMesh(data);
        try
        {
            // Cached asset textures belong to the engine and survive renderer disposal.
            maleTextureId = api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/animal-sex-indicators/male.png"));
            femaleTextureId = api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId, "textures/animal-sex-indicators/female.png"));
            if (!ReloadShader()) throw new InvalidOperationException("Could not compile the animal sex indicator shader.");
            api.Event.ReloadShader += ReloadShader;
            api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "animal-sex-indicators");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Filters visible bred animals, builds camera-facing transforms, and depth-tests each symbol.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var config = VanillaExpandedModSystem.Config;
        if (disposed || shader is null || stage != EnumRenderStage.Opaque || !config.EnableAnimalSexIndicators || api.World.Player is null) return;
        var player = api.World.Player.Entity;
        var camera = player.CameraPos;
        float range = FiniteClamp(config.AnimalSexIndicatorRange, 24, 1, 64);
        float size = FiniteClamp(config.AnimalSexIndicatorSize, 0.25f, 0.05f, 1);
        double[] view = api.Render.CameraMatrixOrigin;
        using var state = new AnimalSexIndicatorRenderState(api.Render);
        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.DepthMask(false);
        // Symbols composite only scene color, leaving glow and SSAO geometry attachments intact.
        for (int i = 1; i <= 3; i++) GL.ColorMask(i, false, false, false, false);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(IndexedEnableCap.Blend, 0);
        GL.BlendEquation(0, BlendEquationMode.FuncAdd);
        GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
        shader.Use();
        try
        {
            // Camera uniforms and activation are shared by every symbol in this frame.
            shader.UniformMatrix("viewMatrix", api.Render.CameraMatrixOriginf);
            shader.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
            foreach (var entity in api.World.LoadedEntities.Values)
            {
                if (!entity.IsRendered || entity.Pos.Dimension != player.Pos.Dimension) continue;
                string? sex = AnimalSexEligibility.GetSex(entity);
                if (sex is null) continue;
                double x = entity.Pos.X - camera.X;
                double y = entity.Pos.InternalY + entity.SelectionBox.Y2 + 0.12 + size / 2 - camera.Y;
                double z = entity.Pos.Z - camera.Z;
                if (x * x + y * y + z * z > range * range) continue;
                // Transpose the view rotation to keep the billboard facing the camera, including pitch.
                model.Identity();
                float[] matrix = model.Values;
                for (int column = 0; column < 3; column++)
                for (int row = 0; row < 3; row++) matrix[column * 4 + row] = (float)view[row * 4 + column] * size;
                matrix[12] = (float)x; matrix[13] = (float)y; matrix[14] = (float)z;
                int color = sex == "male" ? config.AnimalMaleIconColor : config.AnimalFemaleIconColor;
                tint.Set(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f, 1);
                AnimalSexIndicatorDraw.Draw(api.Render, shader, quad,
                    sex == "male" ? maleTextureId : femaleTextureId, matrix, tint);
            }
        }
        finally { shader.Stop(); }
    }

    /// <summary>Releases the owned quad exactly once; the engine owns cached asset textures.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.ReloadShader -= ReloadShader;
        shader?.Dispose();
        shader = null;
        quad.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Compiles a replacement program on shader reload and releases the previous owned program.</summary>
    private bool ReloadShader()
    {
        var replacement = new AnimalSexIndicatorShaderProgram(api);
        try
        {
            api.Shader.RegisterFileShaderProgram("animal_sex_indicator", replacement);
            if (!replacement.Compile())
            {
                replacement.Dispose();
                return false;
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
        shader?.Dispose();
        shader = replacement;
        return true;
    }

    /// <summary>Rejects nonfinite file settings and bounds resource-independent visual controls.</summary>
    private static float FiniteClamp(float value, float fallback, float min, float max)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    #endregion
}
