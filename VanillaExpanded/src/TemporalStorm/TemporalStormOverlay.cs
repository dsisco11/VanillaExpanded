using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Samples the synchronized native storm schedule and owns its passive fractured iris presentation.</summary>
internal sealed class TemporalStormOverlay : IHudOverlay, IHudOverlayBackdropProvider, IHudOverlayVisibilityTransition
{
    private readonly TemporalStormPresentation presentation;
    private ICoreClientAPI? api;
    private SystemTemporalStability? storms;
    private TemporalStormStage? stage;
    private bool enabled = true;
    private bool disposed;

    /// <summary>Controls retained artwork during visibility changes.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;

    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => Clear();
    private bool styleChanged;

    #region Public API
    /// <summary>Retains style invalidation until the next eligible content and layout refresh.</summary>
    public void SetStyle(HudIndicatorStyle style) => styleChanged |= presentation.SetStyle(style);

    #region Lifecycle
    /// <summary>Creates the presentation with optional headless texture loading.</summary>
    public TemporalStormOverlay(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
    {
        presentation = new TemporalStormPresentation(load, loadBackdrop);
    }
    /// <summary>Binds the native storm provider for the current client world.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A temporal storm session is already bound.");
        this.api = api;
        storms = api.ModLoader.GetModSystem<SystemTemporalStability>();
    }
    /// <summary>Starts the artwork exit transition when disabled through live configuration.</summary>
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
        storms = null;
        Clear();
        presentation.Reset();
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
    /// <summary>Allows polling while quiet so later stage can reveal the indicator.</summary>
    public bool IsApplicable()
    {
        bool applicable = enabled && storms != null && api?.World.Player?.Entity?.Alive == true
            && api.World.Config.GetBool("temporalStability", true)
            && api.World.Config.GetString("temporalStorms") != "off";
        if (!applicable) Clear();
        return applicable;
    }
    /// <summary>Copies the synchronized storm stage outside the drawing path.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var next = TemporalStormSource.Read(storms, api!.World.Calendar.TotalDays);
        // Urgency keeps changing within one stage, so update the cached pulse before the
        // stage equality check. Drawing never needs to query the calendar or storm provider.
        if (next.HasValue)
        {
            var data = storms!.StormData;
            // Native waning notifications do not broadcast fresh storm data either. Use the
            // synchronized end deadline and the same 0.02-day window as the base game.
            double remaining = (next == TemporalStormStage.Active
                ? data.stormActiveTotalDays : data.nextStormTotalDays) - api.World.Calendar.TotalDays;
            presentation.SetContent(next.Value, remaining,
                next == TemporalStormStage.Active && remaining < TemporalStormSource.ImminentWindowDays);
        }
        if (next == stage && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        stage = next;
        if (!next.HasValue) presentation.Hide();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Supplies prepared backdrop positions to the shared host pass.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        presentation.CollectBackdrops(renderer, bounds);
    }

    /// <summary>Loads artwork only for a visible storm stage.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (stage.HasValue) presentation.Prepare(context);
    }
    /// <summary>Reports the active icon footprint, or zero during quiet periods.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Draws cached artwork without polling the native storm system.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        presentation.Draw(renderer, bounds, deltaTime);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears the sampled stage and defers artwork release until its exit finishes.</summary>
    private void Clear()
    {
        stage = null;
        presentation.Hide();
    }
    #endregion
}
