using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Samples current local climate temperature independently of the player body-temperature simulation.</summary>
internal sealed class EnvironmentalTemperatureOverlay : IHudOverlay, IHudOverlayVisibilityTransition
{
    private readonly EnvironmentalTemperaturePresentation presentation;
    private ICoreClientAPI? api;
    private bool enabled = true, disposed;

    private bool styleChanged;

    #region Public API
    /// <summary>Retains a style change until the scheduler prepares and measures the new presentation.</summary>
    public void SetStyle(EnvironmentalGaugeStyle style) => styleChanged |= presentation.SetStyle(style);

    #region Lifecycle and configuration
    /// <summary>Gets the last valid local temperature in degrees Celsius.</summary>
    internal float? Temperature { get; private set; }
    /// <summary>Gets the shared visibility transition.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;
    /// <summary>Creates the owned presentation with optional headless dependencies.</summary>
    public EnvironmentalTemperatureOverlay(EnvironmentalTemperaturePresentation? presentation = null) => this.presentation = presentation ?? new();
    /// <summary>Binds one client world without retaining another world's player sample.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("An environmental temperature session is already bound.");
        this.api = api;
    }
    /// <summary>Starts an exit when live configuration disables the indicator.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) HidePresentation();
        return value;
    }
    #endregion
    #region Sampling
    /// <summary>Requires an enabled indicator and a living local player.</summary>
    public bool IsApplicable()
    {
        var player = api?.World.Player;
        bool applicable = enabled && player?.Entity?.Alive == true;
        if (!applicable) HidePresentation();
        return applicable;
    }
    /// <summary>Reads the current local climate and hides unavailable or nonfinite readings.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var climate = api!.World.BlockAccessor.GetClimateAt(api.World.Player.Entity.Pos.AsBlockPos, EnumGetClimateMode.NowValues);
        float? next = climate != null && float.IsFinite(climate.Temperature) ? climate.Temperature : null;
        if (next == Temperature && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        Temperature = next;
        if (next.HasValue) presentation.SetContent(next.Value);
        else presentation.Hide();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Prepares the sampled temperature outside the draw path.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Temperature.HasValue) presentation.Prepare(context);
    }
    /// <summary>Reports the prepared reading and scale footprint.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Draws cached layers without accessing gameplay state.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime) => presentation.Draw(renderer, bounds, clip);
    /// <summary>Clears sampled eligibility while retaining artwork for an exit.</summary>
    public void HidePresentation()
    {
        Temperature = null;
        presentation.Hide();
    }
    #endregion
    #region Cleanup
    /// <summary>Releases borrowed world state and artwork bindings immediately.</summary>
    public void EndSession()
    {
        api = null; Temperature = null; presentation.Reset();
    }
    /// <summary>Ends ownership exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        presentation.Dispose();
    }
    #endregion
    #endregion
}

