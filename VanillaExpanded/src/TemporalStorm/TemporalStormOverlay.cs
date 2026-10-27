using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Samples the synchronized native storm schedule and owns its passive fractured iris presentation.</summary>
internal sealed class TemporalStormOverlay : IHudOverlay, INativePresentation, IHudOverlayVisibilityTransition
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
    /// <summary>Accepts prepared native presentation state for focused integration checks.</summary>
    public TemporalStormOverlay(TemporalStormPresentation? presentation = null)
    {
        this.presentation = presentation ?? new();
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
    /// <summary>Drops borrowed native state and prepared presentation data at world exit.</summary>
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
    /// <summary>Creates unique native visuals without sampling or subscribing.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);

    /// <summary>Advances shared visual effects once before all visual instances render.</summary>
    public void BeginFrame(float deltaTime) => presentation.BeginFrame(deltaTime);

    /// <summary>Prepares localized data and metrics only for a visible storm stage.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (stage.HasValue) presentation.Prepare(context);
    }
    /// <summary>Reports the active icon footprint, or zero during quiet periods.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Requires native rendering through the registered component elements.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        throw new InvalidOperationException("Native indicator components render through their composer.");
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
