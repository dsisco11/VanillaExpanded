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
    private const float DECAL_SIZE = 1.5f;
    private const float FADE_DURATION = 2.0f;
    private const float PULSE_SPEED = 2.0f;
    private const float PULSE_MIN_ALPHA = 0.4f;
    private const float PULSE_MAX_ALPHA = 0.8f;
    #endregion

    #region Fields
    private readonly ICoreClientAPI capi;
    private MeshRef? decalMeshRef;
    private int decalTextureId;
    private readonly Matrixf modelMatrix = new();

    private Vec3d? spawnPosition;
    private bool isFading;
    private float fadeAlpha = 1.0f;
    private float pulseTime;
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
        var meshData = QuadMeshUtil.GetQuad();

        // Transform the quad to lie flat on the ground (rotate around X axis by 90 degrees)
        // and scale it to desired size
        for (int i = 0; i < meshData.VerticesCount; i++)
        {
            int idx = i * 3;
            // Original quad is in XY plane, we want it in XZ plane
            float x = meshData.xyz[idx] * DECAL_SIZE;
            float y = meshData.xyz[idx + 1];
            float z = meshData.xyz[idx + 2] * DECAL_SIZE;

            // Rotate 90 degrees around X: new_y = -old_z, new_z = old_y
            meshData.xyz[idx] = x;
            meshData.xyz[idx + 1] = -z * DECAL_SIZE;
            meshData.xyz[idx + 2] = y * DECAL_SIZE;
        }

        decalMeshRef = capi.Render.UploadMesh(meshData);
    }

    private void LoadTexture()
    {
        // Use the block breaking overlay texture
        var textureLoc = new AssetLocation("game", "textures/environment/blockbreakoverlay.png");
        decalTextureId = capi.Render.GetOrLoadTexture(textureLoc);
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Sets the spawn position to render the decal at.
    /// </summary>
    public void SetSpawnPosition(Vec3d position)
    {
        spawnPosition = position.Clone();
        isFading = false;
        fadeAlpha = 1.0f;
    }

    /// <summary>
    /// Clears the spawn position and begins fade-out animation.
    /// </summary>
    public void ClearSpawnPosition()
    {
        if (spawnPosition != null)
        {
            isFading = true;
        }
    }
    #endregion

    #region IRenderer Implementation
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (spawnPosition == null || decalMeshRef == null)
            return;

        // Handle fade animation
        if (isFading)
        {
            fadeAlpha -= deltaTime / FADE_DURATION;
            if (fadeAlpha <= 0)
            {
                spawnPosition = null;
                isFading = false;
                fadeAlpha = 1.0f;
                return;
            }
        }

        // Calculate pulse effect
        pulseTime += deltaTime * PULSE_SPEED;
        float pulseAlpha = GameMath.Lerp(PULSE_MIN_ALPHA, PULSE_MAX_ALPHA, (GameMath.Sin(pulseTime) + 1f) * 0.5f);
        float finalAlpha = pulseAlpha * fadeAlpha;

        // Get camera position for relative rendering
        var camPos = capi.World.Player.Entity.CameraPos;

        // Build model matrix
        modelMatrix.Identity();
        modelMatrix.Translate(
            (float)(spawnPosition.X - camPos.X),
            (float)(spawnPosition.Y - camPos.Y + 0.0001), // Slight offset above ground to prevent z-fighting
            (float)(spawnPosition.Z - camPos.Z)
        );

        // Render using standard shader
        var shader = capi.Render.PreparedStandardShader(
            (int)spawnPosition.X,
            (int)spawnPosition.Y,
            (int)spawnPosition.Z
        );

        shader.Use();
        shader.Tex2D = decalTextureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.RgbaGlowIn = new Vec4f(0.3f, 0.5f, 1.0f, finalAlpha); // Blue-ish glow
        shader.ExtraGlow = (int)(finalAlpha * 128);

        capi.Render.GlToggleBlend(true);
        capi.Render.RenderMesh(decalMeshRef);
        capi.Render.GlToggleBlend(false);

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
}
