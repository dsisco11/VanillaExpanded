using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.SpawnDecal;

/// <summary>
/// Renders a glowing decal at the player's temporal gear spawn position.
/// </summary>
public class SpawnDecalRenderer : IRenderer
{
    #region Constants
    private const float DECAL_SIZE = 0.4f;
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
    #endregion

    #region IRenderer Properties
    public double RenderOrder => 0.5; // Decal render stage
    public int RenderRange => 100;
    #endregion

    #region Constructor
    public SpawnDecalRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        InitializeMesh();
        LoadTexture();
    }
    #endregion

    #region Initialization
    private void InitializeMesh()
    {
        // Create a flat quad mesh for the decal (lying on the ground)
        var meshData = QuadMeshUtil.GetCustomQuadHorizontal(0.5f, Z_OFFSET, -0.5f, -1f, 1f, 255, 255, 255, 255);
        // multiply all vertex coords by DECAL_SIZE
        float[] verticies = meshData.GetXyz();
        for (int i = 0; i < meshData.VerticesCount; i++)
        {
            verticies[i * 3 + 0] *= DECAL_SIZE;
            verticies[i * 3 + 1] *= DECAL_SIZE;
            verticies[i * 3 + 2] *= DECAL_SIZE;
        }
        meshData.SetXyz(verticies);
        decalMeshRef = capi.Render.UploadMesh(meshData);
    }

    private void LoadTexture()
    {
        // Use the block breaking overlay texture
        var textureLoc = new AssetLocation("vanillaexpanded", "textures/respawnpoint.png");
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
        modelMatrix.Translate(
            (float)(spawnPosition.X - camPos.X),
            (float)(spawnPosition.Y - camPos.Y),
            (float)(spawnPosition.Z - camPos.Z)
        );

        // Render using standard shader
        IStandardShaderProgram shader = rapi.PreparedStandardShader(spawnPosition.XInt, spawnPosition.YInt, spawnPosition.ZInt);
        shader.Use();
        shader.Tex2D = decalTextureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.RgbaTint = FinalRenderGlow;
        shader.RgbaGlowIn = FinalRenderGlow;
        shader.ExtraGlow = (int)strength;

        rapi.GlToggleBlend(true, EnumBlendMode.Overlay);
        rapi.RenderMesh(decalMeshRef);
        rapi.GlToggleBlend(false);

        shader.Stop();
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
