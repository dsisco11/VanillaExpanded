using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL4;

using VanillaExpanded.ItemSlotIndicators.Animation;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Runs one client-wide liquid grid before GUI dialogs, owning scheduling, programs, and lifecycle recovery.</summary>
internal sealed class LiquidSloshSimulation : IRenderer
{
    // Halving cell spacing requires halving the step to retain the same worst-case Courant number.
    private const double StepSeconds = 1.0 / 480;
    private readonly ICoreClientAPI api;
    private readonly Func<ItemSlotIndicatorCameraSample?> camera;
    private readonly LiquidSloshMotionState motion = new();
    private LiquidSloshSimulationShaderProgram? shader;
    private LiquidSloshStateBuffers? buffers;
    private double previousTime, accumulator;
    private bool hasTime, wasEnabled, failed, disposed;

    /// <summary>Runs after shared camera capture at 0.99 and before the GUI manager at one.</summary>
    public double RenderOrder => 0.995;
    /// <summary>Specifies no world-distance restriction for the shared surface.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Gets only a live published surface; zero selects renderer fallback.</summary>
    internal int SurfaceTexture => disposed || failed || shader is not { Disposed: false } ? 0 : buffers?.ReadTexture ?? 0;

    #region Public API
    /// <summary>Prepares once and subscribes update/reload independently of slot drawing.</summary>
    internal LiquidSloshSimulation(ICoreClientAPI api, Func<ItemSlotIndicatorCameraSample?> camera)
    {
        this.api = api;
        this.camera = camera;
        Prepare();
        api.Event.ReloadShader += Reload;
        api.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "liquid-slosh-simulation");
    }

    /// <summary>Advances at fixed timesteps once per GUI frame; additional item draws only read the published buffer.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || stage != EnumRenderStage.Ortho || failed || shader is null || buffers is null) return;
        try
        {
            UpdateFrame();
        }
        catch (Exception exception)
        {
            // Include discontinuity resets in the failure boundary, so unavailable GPU state selects plain fill.
            failed = true;
            api.Logger.Warning("[VanillaExpanded] Liquid simulation unavailable until shader reload: {0}", exception.Message);
        }
    }

    /// <summary>Unsubscribes and disposes owned resources once; callbacks become harmless after shutdown.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
        api.Event.ReloadShader -= Reload;
        if (shader is { Disposed: false }) shader.Dispose();
        shader = null;
        buffers?.Dispose();
        buffers = null;
        motion.Reset();
    }
    #endregion

    #region Private
    /// <summary>Derives camera forcing, resets discontinuities, and advances the shared fixed-step grid.</summary>
    private void UpdateFrame()
    {
        var shader = this.shader!;
        var buffers = this.buffers!;
        double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        double elapsed = hasTime ? now - previousTime : 0;
        previousTime = now;
        bool first = !hasTime;
        hasTime = true;
        bool enabled = VanillaExpandedModSystem.Config.EnableLiquidContainerIndicators;
        if (!enabled)
        {
            if (wasEnabled) buffers.Reset();
            wasEnabled = false;
            motion.Reset();
            accumulator = 0;
            return;
        }
        if (!wasEnabled)
        {
            motion.Reset();
            accumulator = 0;
            wasEnabled = true;
        }
        if (first) return;
        if (motion.Update(elapsed, camera()))
        {
            buffers.Reset();
            accumulator = 0;
        }
        if (elapsed <= 0 || elapsed > 0.25) return;
        accumulator += elapsed;
        if (accumulator < StepSeconds) return;

        var previousProgram = api.Render.CurrentActiveShader;
        try
        {
            // Ortho already has GUI active. Use engine tracking, never GPU state queries, for this handoff.
            previousProgram?.Stop();
            shader.Use();
            shader.ContainerAcceleration = motion.ContainerAcceleration;
            shader.VerticalShapeVariation = motion.VerticalShapeVariation;
            while (accumulator >= StepSeconds)
            {
                shader.SourceStateTexture = buffers.ReadTexture;
                shader.FeedbackBuffer = buffers.WriteBuffer;
                shader.Advance();
                buffers.Swap();
                accumulator -= StepSeconds;
            }
        }
        finally
        {
            if (ReferenceEquals(api.Render.CurrentActiveShader, shader)) shader.Stop();
            previousProgram?.Use();
        }
    }

    /// <summary>Compiles through the engine and allocates fixed state only at startup/reload boundaries.</summary>
    private void Prepare()
    {
        failed = false;
        try
        {
            GL.GetInteger(GetPName.MajorVersion, out int major);
            bool supported = major >= 4;
            if (!supported)
            {
                GL.GetInteger(GetPName.NumExtensions, out int extensions);
                for (int index = 0; index < extensions; index++)
                    supported |= GL.GetString(StringNameIndexed.Extensions, index) == "GL_ARB_transform_feedback2";
            }
            if (!supported) throw new NotSupportedException("Liquid simulation requires transform-feedback objects.");
            buffers ??= new LiquidSloshStateBuffers();
            shader = new LiquidSloshSimulationShaderProgram
            {
                AssetDomain = Constants.ModId,
                VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader),
                FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader),
                TimeStep = (float)StepSeconds,
                CellCount = LiquidSloshStateBuffers.CellCount,
                CellSpacing = 1f / LiquidSloshStateBuffers.CellCount,
                Gravity = 1, Damping = 3,
                WallDamping = 6, WallDampingWidth = 0.2f,
                FeedbackObject = buffers.FeedbackObject,
                VertexArray = buffers.VertexArray
            };
            api.Shader.RegisterFileShaderProgram(LiquidSloshSimulationShaderProgram.ShaderName, shader);
            if (!shader.Compile()) throw new InvalidOperationException("Engine liquid-solver compilation failed.");
            buffers.Reset();
        }
        catch (Exception exception)
        {
            failed = true;
            if (shader is { Disposed: false }) shader.Dispose();
            shader = null;
            api.Logger.Warning("[VanillaExpanded] Liquid simulation unavailable: {0}", exception.Message);
        }
    }

    /// <summary>Invalidates old program references and resets clock/forces before preparing a fresh engine registration.</summary>
    private bool Reload()
    {
        if (disposed) return true;
        if (shader is { Disposed: false }) shader.Dispose();
        shader = null;
        motion.Reset();
        hasTime = wasEnabled = false;
        accumulator = 0;
        Prepare();
        return !failed;
    }
    #endregion
}
