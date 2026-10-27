using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.CalendarDate;

/// <summary>Samples the native date and retains the independent mechanical calendar data.</summary>
internal sealed class CalendarDateOverlay : IHudOverlay, INativePresentation, IHudOverlayVisibilityTransition
{
    private readonly CalendarDatePresentation presentation;
    private ICoreClientAPI? api;
    private CalendarDateReading? date;
    private bool enabled = true, disposed;

    private bool styleChanged;

    #region Public API
    /// <summary>Retains a style change until the scheduler prepares and measures the new presentation.</summary>
    public void SetStyle(EnvironmentalGaugeStyle style) => styleChanged |= presentation.SetStyle(style);

    #region Lifecycle
    /// <summary>Gets the presentation's retained-resource transition.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;
    /// <summary>Creates the feature-owned calendar presentation.</summary>
    public CalendarDateOverlay(CalendarDatePresentation? presentation = null) => this.presentation = presentation ?? new();
    /// <summary>Binds one client world.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A calendar indicator session is already bound.");
        this.api = api;
    }
    /// <summary>Applies feature visibility immediately and retains contents during an exit.</summary>
    public bool SetEnabled(bool value)
    {
        enabled = value;
        if (!value) HidePresentation();
        return value;
    }
    /// <summary>Ends a world binding and releases presentation resources immediately.</summary>
    public void EndSession()
    {
        api = null; date = null; presentation.Reset();
    }
    /// <summary>Releases ownership exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession(); presentation.Dispose();
    }
    #endregion
    #region Sampling and rendering
    /// <summary>Requires an enabled indicator and a living local character.</summary>
    public bool IsApplicable()
    {
        bool applicable = !disposed && enabled && api?.World.Player?.Entity?.Alive == true;
        if (!applicable) HidePresentation();
        return applicable;
    }
    /// <summary>Samples calendar fields outside drawing, invalidating only when the displayed date changes.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var next = CalendarDateReading.Read(api!.World.Calendar);
        if (next == date && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        date = next;
        if (next.HasValue) presentation.SetContent(next.Value);
        else presentation.Hide();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    /// <summary>Prepares localized lettering only when valid native date content is available.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (date.HasValue) presentation.Prepare(context);
    }
    /// <summary>Returns the complete mechanical instrument footprint.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Creates one independent native composition over the feature's prepared data.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);
    /// <summary>Has no elapsed visual effect; the estimated state advances only through scheduled sampling.</summary>
    public void BeginFrame(float deltaTime) { }
    /// <summary>Rejects manual dispatch because registered native components own rendering.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime) =>
        throw new InvalidOperationException("Render this indicator through its native view.");
    /// <summary>Clears sampling state while retaining the prepared date through a fade.</summary>
    public void HidePresentation()
    {
        date = null;
        presentation.Hide();
    }
    #endregion
    #endregion
}
