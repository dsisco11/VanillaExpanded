using System;

using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;
using VanillaExpanded.CrucibleIndicators;

using OpenTK.Graphics.OpenGL4;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Draws effects and equivalent rectangles inside the supported engine GUI pass with owned scratch storage.</summary>
internal sealed class ItemSlotIndicatorDrawBackend : IItemSlotIndicatorDrawBackend
{
    private readonly ICoreClientAPI api;
    private readonly Func<int> liquidSurface;
    private readonly Func<int> foodState;
    private readonly Func<int> metalSurface;
    private readonly Func<int> metalChunks;
    private readonly ItemSlotIndicatorDrawState drawState;
    private readonly float[] rectangleMatrix = new float[16];
    private IShaderProgram? gui;
    private bool captured, rectangleDrawn, simulationStateBound, disposed, failureReported;

    #region Public API
    #region Lifetime
    /// <summary>Owns only scratch storage; programs and meshes remain with the resource caches.</summary>
    internal ItemSlotIndicatorDrawBackend(ICoreClientAPI api, Func<int>? liquidSurface = null, Func<int>? foodState = null,
        Func<int>? metalSurface = null, Func<int>? metalChunks = null)
    {
        this.api = api;
        this.liquidSurface = liquidSurface ?? (static () => 0);
        this.foodState = foodState ?? (static () => 0);
        this.metalSurface = metalSurface ?? (static () => 0);
        this.metalChunks = metalChunks ?? (static () => 0);
        drawState = new(api.Render);
    }

    /// <summary>Drops the borrowed GUI program safely on repeated cleanup.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        gui = null;
    }
    #endregion

    #region Draw Submission and State
    /// <summary>Restricts switching to the live host GUI program; unexpected/null shaders skip only the indicator.</summary>
    public bool Supported => !disposed && api.Render.CurrentActiveShader is { Disposed: false } active
        && ReferenceEquals(active, api.Render.GetEngineShader(EnumShaderProgram.Gui))
        && ItemSlotIndicatorDrawInput.ValidMatrix(api.Render.CurrentProjectionMatrix)
        && ItemSlotIndicatorDrawInput.ValidMatrix(api.Render.CurrentModelviewMatrix);

    /// <summary>Captures only depth/cull state at the slot-grid hook; both draw paths retain inherited clipping.</summary>
    public void Begin()
    {
        gui = api.Render.CurrentActiveShader;
        rectangleDrawn = false;
        simulationStateBound = false;
        drawState.Capture();
        captured = true;
        drawState.Apply();
    }

    /// <summary>Uses engine Stop/Use tracking and uploads only uniforms on an already prepared program.</summary>
    public void Effect(IShaderProgram program, MeshRef mesh, ItemSlotIndicatorDrawInput input,
        ItemSlotIndicatorEffectDefinition definition, ItemSlotIndicatorFrameSnapshot frame)
    {
        bool metal = definition.ShaderName == CrucibleIndicatorEffect.Molten.ShaderName;
        bool liquid = definition.ShaderName == LiquidSloshIndicatorEffect.Definition.ShaderName || metal;
        bool food = definition.ShaderName == FoodGrainIndicatorEffect.Definition.ShaderName;
        bool grains = food || definition.ShaderName == CrucibleIndicatorEffect.Solid.ShaderName;
        int surface = metal ? metalSurface() : liquid ? liquidSurface() : food ? foodState() : grains ? metalChunks() : 0;
        if ((liquid || grains) && surface == 0) throw new InvalidOperationException("Shared indicator simulation is unavailable.");
        gui!.Stop();
        program.Use();
        program.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
        program.UniformMatrix("modelViewMatrix", api.Render.CurrentModelviewMatrix);
        var bounds = input.SlotBounds;
        program.Uniform("slotBounds", bounds.X, bounds.Y, bounds.Z, bounds.W);
        program.Uniform("fill", input.Fill);
        // Particle quantity follows contents, independently of the provider's bounded draw height.
        if (grains) program.Uniform("resourceFill", input.ResourceFill);
        if (program.HasUniform("color")) program.Uniform("color", input.Color.X, input.Color.Y, input.Color.Z, input.Color.W);
        if (program.HasUniform("timeSeconds")) program.Uniform("timeSeconds", frame.TimeSeconds);
        if (program.HasUniform("motion")) program.Uniform("motion", frame.Motion.X, frame.Motion.Y);
        if (program.HasUniform("cameraBob")) program.Uniform("cameraBob", frame.CameraBob);
        if (program.HasUniform("effectParameters"))
        {
            var parameters = definition.Parameters;
            program.Uniform("effectParameters", parameters.X, parameters.Y, parameters.Z, parameters.W);
        }
        if (program.HasUniform("segmentCount")) program.Uniform("segmentCount", definition.SegmentCount);
        if (food) (input.ParticlePalette ?? ItemSlotIndicatorParticlePalette.Default).Submit(program);
        if (grains && !food)
            (input.ParticlePalette ?? throw new InvalidOperationException("Solid crucible particle colors are unavailable."))
                .Submit(program, "metalPalette");
        if (liquid || grains)
        {
            program.Uniform(liquid ? "liquidSurface" : "grainState", 0);
            if (liquid) program.Uniform("surfaceCellCount", LiquidSloshStateBuffers.CellCount);
            simulationStateBound = true;
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.TextureBuffer, surface);
        }
        api.Render.RenderMesh(mesh);
    }

    /// <summary>Draws a texture-free rectangle with the engine GUI shader, legacy rounding, and local depth.</summary>
    public void Rectangle(MeshRef mesh, ItemSlotIndicatorDrawInput input)
    {
        rectangleDrawn = true;
        var color = input.Color;
        gui!.Uniform("rgbaIn", color.X * color.W, color.Y * color.W, color.Z * color.W, color.W);
        gui.Uniform("extraGlow", 0);
        gui.Uniform("applyColor", 0);
        gui.Uniform("noTexture", 1f);
        gui.Uniform("overlayOpacity", 0f);
        gui.Uniform("normalShaded", 0);
        input.RectangleMatrix(api.Render.CurrentModelviewMatrix, rectangleMatrix);
        gui.UniformMatrix("projectionMatrix", api.Render.CurrentProjectionMatrix);
        gui.UniformMatrix("modelViewMatrix", rectangleMatrix);
        api.Render.RenderMesh(mesh);
    }

    /// <summary>Returns to the engine GUI program and its slot-grid transform before restoring depth/cull and standard blending.</summary>
    public void Restore()
    {
        if (!captured) return;
        captured = false;
        try
        {
            if (simulationStateBound)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.TextureBuffer, 0);
            }
            if (!ReferenceEquals(api.Render.CurrentActiveShader, gui))
            {
                api.Render.CurrentActiveShader?.Stop();
                gui!.Use();
            }
            // GUI Use rebinds engine UBOs and sets its standard light direction. Effect draws leave
            // GUI uniforms intact. Only rectangles overwrite texture mode and the model-view transform.
            if (rectangleDrawn)
            {
                gui!.Uniform("noTexture", 0f);
                gui.UniformMatrix("modelViewMatrix", api.Render.CurrentModelviewMatrix);
            }
        }
        finally { drawState.Restore(); }
    }

    /// <summary>Bounds diagnostics for an unusable host GUI boundary without disguising effect availability failures.</summary>
    public void ReportFailure(Exception exception)
    {
        if (failureReported) return;
        failureReported = true;
        api.Logger.Warning("[VanillaExpanded] Indicator GUI draw failed: {0}", exception.Message);
    }
    #endregion
    #endregion
}
