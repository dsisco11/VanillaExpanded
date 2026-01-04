using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.RadialProgress;

/// <summary>
/// Renders a radial (circular) progress bar using GLSL shaders.
/// Uses a packed data texture for high-precision angle/radius lookup.
/// </summary>
public sealed class RadialProgressBarRenderer : IRenderer
{
    #region Constants
    private const string PackedTextureSamplerName = "packedTex";
    #endregion

    #region Fields
    private readonly ICoreClientAPI capi;
    private readonly IShaderProgram? shader;
    private readonly Matrixf mvMatrix = new();
    private bool isDisposed;
    #endregion

    #region Properties
    /// <summary>
    /// Progress value from 0 (empty) to 1 (full).
    /// </summary>
    public float Progress { get; set; }

    /// <summary>
    /// Outer radius of the ring, from 0 to 1 (1 = edge of inscribed circle).
    /// </summary>
    public float OuterRadius { get; set; } = 1.0f;

    /// <summary>
    /// Inner radius of the ring, from 0 to 1 (0 = solid disc, >0 = ring).
    /// </summary>
    public float InnerRadius { get; set; }

    /// <summary>
    /// Tint color applied to the progress bar (RGBA).
    /// </summary>
    public Vec4f TintColor { get; set; } = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// Screen X position in pixels (left edge).
    /// </summary>
    public float ScreenX { get; set; }

    /// <summary>
    /// Screen Y position in pixels (top edge).
    /// </summary>
    public float ScreenY { get; set; }

    /// <summary>
    /// Width in pixels.
    /// </summary>
    public float Width { get; set; } = 100f;

    /// <summary>
    /// Height in pixels.
    /// </summary>
    public float Height { get; set; } = 100f;

    /// <summary>
    /// Whether this renderer is currently enabled and should draw.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <inheritdoc />
    public double RenderOrder => 0.9;

    /// <inheritdoc />
    public int RenderRange => 10;
    #endregion

    #region Constructor
    /// <summary>
    /// Creates a new radial progress bar renderer.
    /// </summary>
    /// <param name="api">The client API.</param>
    /// <param name="startOffset01">Start angle offset in [0,1] range (0 = +X/right, 0.25 = +Y/top, etc.).</param>
    /// <param name="clockwise">True for clockwise fill direction.</param>
    /// <exception cref="InvalidOperationException">Thrown if resources fail to initialize.</exception>
    public RadialProgressBarRenderer(ICoreClientAPI api, float startOffset01 = 0.25f, bool clockwise = true)
    {
        capi = api ?? throw new ArgumentNullException(nameof(api));

        if (!RadialProgressResources.Initialize(api))
        {
            throw new InvalidOperationException("Failed to initialize radial progress resources.");
        }

        shader = RadialProgressResources.GetOrCreateShader(startOffset01, clockwise);
        if (shader is null)
        {
            RadialProgressResources.Release();
            throw new InvalidOperationException("Failed to compile radial progress shader.");
        }
    }
    #endregion

    #region IRenderer Implementation
    /// <inheritdoc />
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!Enabled || isDisposed || shader is null || !RadialProgressResources.IsInitialized)
        {
            return;
        }

        var quadMesh = RadialProgressResources.QuadMesh;
        if (quadMesh is null)
        {
            return;
        }

        // Get the current active shader to restore later
        var prevShader = capi.Render.CurrentActiveShader;
        prevShader?.Stop();

        try
        {
            shader.Use();

            // Bind the packed data texture
            shader.BindTexture2D(PackedTextureSamplerName, RadialProgressResources.TextureId, 0);

            // Set uniforms
            shader.Uniform("progressScalar", Progress);
            shader.Uniform("outerRadius", OuterRadius);
            shader.Uniform("innerRadius", InnerRadius);
            shader.Uniform("tintColor", TintColor);

            // Build model-view matrix for screen-space positioning
            // QuadMeshUtil.GetQuad() produces vertices in [-1,1] range
            // We need to transform to screen coordinates
            mvMatrix
                .Set(capi.Render.CurrentModelviewMatrix)
                .Translate(ScreenX, ScreenY, 50f)
                .Scale(Width, Height, 0f)
                .Translate(0.5f, 0.5f, 0f)
                .Scale(0.5f, 0.5f, 0f);

            shader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", mvMatrix.Values);

            // Render the quad
            capi.Render.RenderMesh(quadMesh);
        }
        finally
        {
            shader.Stop();
            prevShader?.Use();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        RadialProgressResources.Release();
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Sets the screen rectangle for rendering.
    /// </summary>
    /// <param name="x">X position in pixels.</param>
    /// <param name="y">Y position in pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public void SetRect(float x, float y, float width, float height)
    {
        ScreenX = x;
        ScreenY = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Sets the ring parameters.
    /// </summary>
    /// <param name="inner">Inner radius (0 = solid disc).</param>
    /// <param name="outer">Outer radius (1 = full size).</param>
    public void SetRadii(float inner, float outer)
    {
        InnerRadius = inner;
        OuterRadius = outer;
    }
    #endregion
}
