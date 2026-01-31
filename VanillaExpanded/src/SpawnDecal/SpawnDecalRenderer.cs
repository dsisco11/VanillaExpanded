using System;

using OpenTK.Graphics.OpenGL4;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace VanillaExpanded.SpawnDecal;

/// <summary>
/// Renders a glowing decal at the player's temporal gear spawn position.
/// </summary>
public class SpawnDecalRenderer : IRenderer
{
    #region GL State
    private const int OitAccumulationColorAttachmentIndex = 0;

    private static void RestoreOitBuf0BlendState()
    {
        // In Vintage Story's OIT pass, buf0 is the accumulation target and expects additive blending.
        GL.BlendEquation(OitAccumulationColorAttachmentIndex, BlendEquationMode.FuncAdd);
        GL.BlendFuncSeparate(OitAccumulationColorAttachmentIndex, BlendingFactorSrc.One, BlendingFactorDest.One, BlendingFactorSrc.One, BlendingFactorDest.One);
    }

    private static void ApplyDecalBuf0BlendState()
    {
        // Only touch buf0. Do not modify other MRT targets.
        // Standard alpha blending for the decal texture.
        GL.BlendEquation(OitAccumulationColorAttachmentIndex, BlendEquationMode.FuncAdd);
        GL.BlendFuncSeparate(OitAccumulationColorAttachmentIndex, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.One);
    }
    #endregion

    #region Constants
    private float DecalSize => VanillaExpandedModSystem.Config.SpawnDecalSize;
    private const float Z_OFFSET = 0.0001f;
    private const float FADE_DURATION = 1f / 2f;
    private const float COLOR_PHASE_DURATION = 1f / 5f;
    private const float STRENGTH_PHASE_DURATION = 1f / 13f;
    private const float PULSE_MIN_STRENGTH = 10f;
    private const float PULSE_MAX_STRENGTH = 80f;
    private const float PULSE_STRENGTH_RANGE = PULSE_MAX_STRENGTH - PULSE_MIN_STRENGTH;
    #endregion

    #region Fields
    private readonly ICoreClientAPI capi;
    private MeshRef? decalMeshRef;
    private int decalTextureId;
    private readonly Matrixf modelMatrix = new();
    private readonly System.Numerics.Vector4[] PhaseColors = [new(0.28f, 0.8f, 1.0f, 1.0f), new(0.7f, 0.28f, 1.0f, 1.0f)];

    private Vec3d? spawnPosition;
    private bool isFading;
    private float fadeAlpha = 1.0f;
    private float colorPhaseTime = 0f;
    private float strengthPhaseTime = 0f;
    private Vec4f FinalRenderGlow = new();
    /// <summary>
    /// Used to 'unset' the glow after rendering. (otherwise the glow color state bleeds into other renders)
    /// </summary>
    private Vec4f DefaultRenderGlow = ColorUtilEx.TransparentWhiteRgbaVec;
    #endregion

    #region IRenderer Properties
    public double RenderOrder => 0.7f; // Decal render stage
    public int RenderRange => 32;
    #endregion

    #region Constructor
    public SpawnDecalRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, "spawndecal");
        InitializeMesh();
        LoadTexture();
    }
    #endregion

    #region Initialization
    private void InitializeMesh()
    {
        decalMeshRef?.Dispose();

        // Create a flat quad mesh for the decal (lying on the ground)
        var meshData = QuadMeshUtil.GetCustomQuadHorizontal(0.5f, Z_OFFSET, -0.5f, -1f, 1f, 255, 255, 255, 255);
        // multiply all vertex coords by DecalSize
        float[] verticies = meshData.GetXyz();
        float decalSize = DecalSize;
        for (int i = 0; i < meshData.VerticesCount; i++)
        {
            verticies[i * 3 + 0] *= decalSize;
            verticies[i * 3 + 1] *= decalSize;
            verticies[i * 3 + 2] *= decalSize;
        }
        meshData.SetXyz(verticies);
        decalMeshRef = capi.Render.UploadMesh(meshData);
    }

    public void ReloadMesh()
    {
        InitializeMesh();
    }

    private void LoadTexture()
    {
        // Use the block breaking overlay texture
        var textureLoc = new AssetLocation(Constants.ModId, "textures/respawnpoint.png");
        decalTextureId = capi.Render.GetOrLoadTexture(textureLoc);
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Sets the spawn position to render the decal at.
    /// </summary>
    public void SetSpawnPosition(Vec3d position)
    {
        spawnPosition = position.Clone(); // Slight offset above ground to prevent z-fighting
        isFading = false;
        fadeAlpha = 1.0f;
    }

    /// <summary>
    /// Clears the spawn position and begins fade-out animation.
    /// </summary>
    public void ClearSpawnPosition()
    {
        if (spawnPosition is not null)
        {
            isFading = true;
        }
    }
    #endregion

    #region IRenderer Implementation
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (spawnPosition is null || decalMeshRef is null)
            return;

        // Handle fade animation
        if (isFading)
        {
            fadeAlpha -= deltaTime * FADE_DURATION;
            if (fadeAlpha <= 0)
            {
                RemoveDecal();
                return;
            }
        }
        IRenderAPI rapi = capi.Render;

        // Calculate pulse effect
        float colorPulseDelta = deltaTime * COLOR_PHASE_DURATION;
        float strengthPulseDelta = deltaTime * STRENGTH_PHASE_DURATION;
        colorPhaseTime = (colorPhaseTime + colorPulseDelta) % 1f;
        strengthPhaseTime = (strengthPhaseTime + strengthPulseDelta) % 1f;
        float colorPhase = (float)((Math.Sin(colorPhaseTime * Math.PI * 2) + 1) / 2); // Normalize to [0,1]
        float strengthPhase = (float)((Math.Sin(strengthPhaseTime * Math.PI * 2) + 1) / 2); // Normalize to [0,1]

        // Lerp the pulse strength
        float strength = PULSE_MIN_STRENGTH + (PULSE_STRENGTH_RANGE * strengthPhase);

        // Lerp the phase colors to get final glow
        var finalColor = System.Numerics.Vector4.Lerp(PhaseColors[0], PhaseColors[1], colorPhase);
        FinalRenderGlow.Set(finalColor);

        // Get camera position for relative rendering
        var camPos = capi.World.Player.Entity.CameraPos;

        // Build model matrix
        modelMatrix.Identity();
        // TODO: it shouldn't be necessary to calculate the translation compared to the camera pos like this, the gpu transform matrix should already be inverse translated by the camera pos?
        modelMatrix.Translate(
            (float)(spawnPosition.X - camPos.X),
            (float)(spawnPosition.Y - camPos.Y),
            (float)(spawnPosition.Z - camPos.Z)
        );

        bool debugGroupPushed = TryPushGlDebugGroup("VanillaExpanded: SpawnDecalRenderer");
        try
        {
            // Render using standard shader
            IStandardShaderProgram shader = rapi.PreparedStandardShader(spawnPosition.XInt, spawnPosition.YInt, spawnPosition.ZInt);
            shader.Use();
            shader.Tex2D = decalTextureId;
            shader.ModelMatrix = modelMatrix.Values;
            shader.RgbaTint = FinalRenderGlow;
            shader.RgbaGlowIn = FinalRenderGlow;
            shader.ExtraGlow = (int)strength;

            ApplyDecalBuf0BlendState();
            rapi.RenderMesh(decalMeshRef);

            // Reset shader inputs to defaults to prevent affecting subsequent renders (e.g., particles)
            shader.RgbaTint = ColorUtil.WhiteArgbVec;  // Reset tint to white
            shader.RgbaGlowIn = DefaultRenderGlow;  // No glow
            shader.ExtraGlow = 0;  // No extra glow
            shader.Stop();
        }
        finally
        {
            RestoreOitBuf0BlendState();
            if (debugGroupPushed) TryPopGlDebugGroup();
        }
    }

    private static bool TryPushGlDebugGroup(string message)
    {
        #if DEBUG
        try
        {
            GL.PushDebugGroup(DebugSourceExternal.DebugSourceApplication, 0, message.Length, message);
            return true;
        }
        catch
        {
            return false;
        }
        #endif
    }

    private static void TryPopGlDebugGroup()
    {
        #if DEBUG
        try
        {
            GL.PopDebugGroup();
        }
        catch
        {
            // ignored
        }
        #endif
    }
    #endregion

    #region IDisposable
    public void Dispose()
    {
        decalMeshRef?.Dispose();
        decalMeshRef = null;
    }
    #endregion

    #region Private Methods
    private void RemoveDecal()
    {
        spawnPosition = null;
        isFading = false;
        fadeAlpha = 1.0f;
    }
    #endregion
}
