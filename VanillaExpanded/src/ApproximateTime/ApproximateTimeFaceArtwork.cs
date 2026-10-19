using System;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Owns GPU composition of the cached clock housing, solar face, and seasonal scenery.</summary>
internal sealed class ApproximateTimeFaceArtwork : IDisposable
{
    private ICoreClientAPI? api;
    private ApproximateTimeFaceShaderProgram? shader;
    private LoadedTexture[]? layers;
    private MeshRef? quad;
    private FrameBufferRef? target;
    private LoadedTexture? texture;

    #region Public API
    /// <summary>Indicates that a shader reload or reset requires another cached composition.</summary>
    public bool Dirty { get; private set; } = true;

    /// <summary>Renders changed solar metadata into persistent GPU storage and returns its borrowed texture.</summary>
    public LoadedTexture Create(HudOverlayPreparationContext preparation, ApproximateTimeDayPhases phases)
    {
        if (!ReferenceEquals(api, preparation.Api))
        {
            Reset();
            api = preparation.Api;
            api.Event.ReloadShader += ReloadShader;
        }
        using var state = new ApproximateTimeFaceRenderState(api.Render);
        try
        {
            state.ConfigureComposition();
            EnsureResources(preparation);
            api.Render.CurrentFrameBuffer = target!;
            api.Render.GlViewport(0, 0, target!.Width, target.Height);
            api.Render.CurrentActiveShader?.Stop();
            shader!.Use();
            shader.HousingTextureId = layers![0].TextureId;
            shader.DaySceneryTextureId = layers[1].TextureId;
            shader.NightSceneryTextureId = layers[2].TextureId;
            shader.FaceGeometry = new Vec4f(RadialGaugeLayout.CenterX, RadialGaugeLayout.CenterY,
                RadialGaugeLayout.ApertureRadius - 2, RadialGaugeLayout.Canvas);
            shader.SegmentInset = .002f;
            shader.SetDistribution(phases);
            api.Render.RenderMesh(quad!);
            shader.Stop();
            HudOverlayTextureFiltering.Prepare(texture!);
            Dirty = false;
            return texture!;
        }
        catch { Dirty = true; throw; }
    }

    /// <summary>Unsubscribes reload handling and releases owned resources while dropping borrowed artwork.</summary>
    public void Reset()
    {
        api?.Event.ReloadShader -= ReloadShader;
        if (target != null) api!.Render.DestroyFrameBuffer(target);
        target = null;
        texture?.Dispose(); texture = null;
        shader?.Dispose(); shader = null;
        if (quad != null) api!.Render.DeleteMesh(quad);
        quad = null; layers = null; api = null; Dirty = true;
    }

    /// <summary>Releases resources when the presentation ends.</summary>
    public void Dispose() => Reset();
    #endregion

    #region Private
    /// <summary>Allocates geometry and storage once per client binding, borrowing source PNG textures.</summary>
    private void EnsureResources(HudOverlayPreparationContext preparation)
    {
        if (shader == null && !ReloadShader()) throw new InvalidOperationException("Could not compile the clock face shader.");
        layers ??= WorkshopGaugeArtwork.Load(preparation, RadialGaugeLayout.Backing, "clock/day-scenery", "clock/night-scenery");
        if (quad == null)
        {
            var mesh = new MeshData(4, 6, withRgba: false, withFlags: false);
            mesh.AddVertex(-1, -1, 0, 0, 0);
            mesh.AddVertex(1, -1, 0, 1, 0);
            mesh.AddVertex(1, 1, 0, 1, 1);
            mesh.AddVertex(-1, 1, 0, 0, 1);
            foreach (int index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.AddIndex(index);
            quad = api!.Render.UploadMesh(mesh);
        }
        if (target == null)
        {
            var attributes = new FramebufferAttrs("vanillaexpanded-clock-face", RadialGaugeLayout.Canvas, RadialGaugeLayout.Canvas)
            {
                Attachments =
                [
                    new FramebufferAttrsAttachment
                    {
                        AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                        Texture = new RawTexture { Width = RadialGaugeLayout.Canvas, Height = RadialGaugeLayout.Canvas }
                    }
                ]
            };
            target = api!.Render.CreateFrameBuffer(attributes);
            // The public DestroyFrameBuffer API deletes only the FBO; this owner must release its color texture.
            texture = new LoadedTexture(api) { TextureId = target.ColorTextureIds[0], Width = target.Width,
                Height = target.Height };
        }
    }

    /// <summary>Recompiles the file shader and invalidates the cached composition for the next preparation.</summary>
    private bool ReloadShader()
    {
        Dirty = true;
        shader?.Dispose(); shader = null;
        var program = new ApproximateTimeFaceShaderProgram(api!);
        try
        {
            api!.Shader.RegisterFileShaderProgram(ApproximateTimeFaceShaderProgram.ShaderName, program);
            if (!program.Compile()) { program.Dispose(); return false; }
            shader = program;
            return true;
        }
        catch { program.Dispose(); throw; }
    }
    #endregion
}
