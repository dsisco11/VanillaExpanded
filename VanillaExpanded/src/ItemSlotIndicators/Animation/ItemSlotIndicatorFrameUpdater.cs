using System;
using System.Diagnostics;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Updates one shared animation snapshot before the GUI manager, draws nothing, and owns its callback lifetime.</summary>
internal sealed class ItemSlotIndicatorFrameUpdater : IRenderer
{
    private readonly IClientEventAPI events;
    private readonly IItemSlotIndicatorCameraSource camera;
    private readonly Func<bool> needsCameraMotion;
    private readonly Func<double> clock;
    private bool disposed;

    /// <summary>Gets the shared animation owner consumed by every slot draw.</summary>
    internal ItemSlotIndicatorFrameState State { get; } = new();
    /// <summary>Runs immediately before the engine GUI manager at order one.</summary>
    public double RenderOrder => 0.99;
    /// <summary>Specifies no world-distance restriction for GUI animation updates.</summary>
    public int RenderRange => int.MaxValue;

    #region Public API
    /// <summary>Registers exactly one Ortho callback; an injected monotonic clock enables deterministic verification.</summary>
    internal ItemSlotIndicatorFrameUpdater(IClientEventAPI events, IItemSlotIndicatorCameraSource camera,
        Func<bool> needsCameraMotion, Func<double>? clock = null)
    {
        this.events = events;
        this.camera = camera;
        this.needsCameraMotion = needsCameraMotion;
        this.clock = clock ?? (static () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
        events.RegisterRenderer(this, EnumRenderStage.Ortho, "itemslotindicator-frame");
    }

    /// <summary>Publishes a frame snapshot only at Ortho; item-render delta time and provider queries are irrelevant.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || stage != EnumRenderStage.Ortho) return;
        bool needsMotion = needsCameraMotion();
        State.Update(clock(), needsMotion, needsMotion ? camera.Capture() : null);
    }

    /// <summary>Removes the callback once; later engine callbacks cannot advance a disposed owner.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        events.UnregisterRenderer(this, EnumRenderStage.Ortho);
    }
    #endregion
}
