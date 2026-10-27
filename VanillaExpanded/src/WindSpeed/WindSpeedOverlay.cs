using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.WindSpeed;

/// <summary>Samples native local wind and owns its HUD presentation.</summary>
internal sealed class WindSpeedOverlay : IHudOverlay, INativePresentation, IHudOverlayVisibilityTransition
{
    private readonly WindSpeedPresentation presentation;
    private readonly WindSpeedAverage average = new();
    private ICoreClientAPI? api;
    private Entity? sampledEntity;
    private bool enabled = true, disposed, styleChanged;

    #region Public API
    #region Lifecycle and configuration
    /// <summary>Gets the last classified wind strength, with zero representing calm or unavailable wind.</summary>
    internal int Intensity { get; private set; }
    /// <summary>Gets the recent-average wind percentage using the native windmill calculation.</summary>
    internal int Percentage { get; private set; }
    /// <summary>Gets the shared visibility transition used by the host.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;

    /// <summary>Creates an owned wind presentation with an optional headless substitute.</summary>
    public WindSpeedOverlay(WindSpeedPresentation? presentation = null)
    {
        this.presentation = presentation ?? new();
    }

    /// <summary>Retains style invalidation until the next eligible preparation and measurement.</summary>
    public void SetStyle(HudIndicatorStyle style) => styleChanged |= presentation.SetStyle(style);

    /// <summary>Binds one client world without reusing another world's wind history.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A wind speed session is already bound.");
        this.api = api;
    }

    /// <summary>Starts the shared exit transition when live configuration disables wind.</summary>
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
        bool applicable = enabled && api?.World.Player?.Entity?.Alive == true;
        if (!applicable) HidePresentation();
        return applicable;
    }

    /// <summary>Averages recent native wind magnitudes before updating the percentage and visibility outside drawing.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var entity = api!.World.Player.Entity;
        var wind = api.World.BlockAccessor.GetWindSpeedAt(entity.Pos.XYZ);
        // Samples and transition history belong to one living player in one client world.
        if (!ReferenceEquals(sampledEntity, entity)) average.Reset();
        double speed = average.Update(wind == null ? double.NaN : wind.Length(), api.World.ElapsedMilliseconds);
        int percentage = WindSpeedSample.ToPercentage(speed);
        // Transition history belongs to the sampled entity, never a replacement player or a new world.
        int next = WindSpeedSample.Classify((float)speed,
            ReferenceEquals(sampledEntity, entity) ? Intensity : 0);
        sampledEntity = entity;
        if (next == Intensity && percentage == Percentage && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        Intensity = next;
        Percentage = percentage;
        if (next > 0) presentation.SetContent(percentage);
        else presentation.Hide();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion

    #region Presentation
    /// <summary>Creates unique native visuals without sampling or subscribing.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);

    /// <summary>Advances shared visual effects once before all visual instances render.</summary>
    public void BeginFrame(float deltaTime) { }

    /// <summary>Prepares localized data and metrics only while wind is active.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Intensity > 0) presentation.Prepare(context);
    }

    /// <summary>Reports the prepared wind footprint to the shared layout host.</summary>
    public SizeF Measure() => presentation.Size;

    /// <summary>Requires native rendering through the registered component elements.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        throw new InvalidOperationException("Native indicator components render through their composer.");
    }

    /// <summary>Clears eligibility and hysteresis while retaining prepared content for the exit transition.</summary>
    public void HidePresentation()
    {
        Intensity = 0;
        Percentage = 0;
        sampledEntity = null;
        average.Reset();
        presentation.Hide();
    }
    #endregion

    #region Cleanup
    /// <summary>Clears world bindings, wind history and prepared presentation data at world exit.</summary>
    public void EndSession()
    {
        api = null;
        sampledEntity = null;
        average.Reset();
        Intensity = 0;
        Percentage = 0;
        presentation.Reset();
    }

    /// <summary>Ends overlay ownership exactly once.</summary>
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
