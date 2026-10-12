using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.RiftActivity;

/// <summary>Samples synchronized native rift activity and owns its passive fissure presentation.</summary>
internal sealed class RiftActivityOverlay : IHudOverlay, IHudOverlayBackdropProvider
{
    private readonly RiftActivityPresentation presentation;
    private ICoreClientAPI? api;
    private ModSystemRiftWeather? weather;
    private RiftActivityLevel? activity;
    private bool enabled = true;
    private bool disposed;

    #region Public API
    #region Lifecycle
    /// <summary>Creates the presentation with optional headless texture loading.</summary>
    public RiftActivityOverlay(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
    {
        presentation = new RiftActivityPresentation(load, loadBackdrop);
    }
    /// <summary>Binds the native packet provider for the current client world.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A rift activity session is already bound.");
        this.api = api;
        weather = api.ModLoader.GetModSystem<ModSystemRiftWeather>();
    }
    /// <summary>Clears cached artwork immediately when disabled through live configuration.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Drops borrowed native state and artwork at world exit.</summary>
    public void EndSession()
    {
        api = null;
        weather = null;
        Clear();
    }
    /// <summary>Releases session bindings exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        presentation.Dispose();
    }
    #endregion
    #region Sampling
    /// <summary>Allows polling while calm so later activity can reveal the indicator.</summary>
    public bool IsApplicable()
    {
        bool applicable = enabled && weather?.Enabled == true && api?.World.Player?.Entity?.Alive == true;
        if (!applicable) Clear();
        return applicable;
    }
    /// <summary>Copies the received activity category outside the drawing path.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var next = RiftActivitySource.Read(weather);
        if (next == activity) return HudOverlayChange.None;
        activity = next;
        if (next.HasValue) presentation.SetContent(next.Value);
        else presentation.Reset();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Supplies prepared backdrop positions to the shared host pass.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        presentation.CollectBackdrops(renderer, bounds);
    }

    /// <summary>Loads artwork only for a visible activity category.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (activity.HasValue) presentation.Prepare(context);
    }
    /// <summary>Reports the active icon footprint, or zero during calm activity.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Draws cached artwork without polling the native activity system.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        presentation.Draw(renderer, bounds);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears cached activity and releases borrowed texture references.</summary>
    private void Clear()
    {
        activity = null;
        presentation.Reset();
    }
    #endregion
}
