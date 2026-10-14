using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Reads synchronized observation age and owns the cached approximate time icon.</summary>
internal sealed class ApproximateTimeOverlay : IHudOverlay, IHudOverlayVisibilityTransition
{

    private readonly ApproximateTimePresentation presentation;
    private ICoreClientAPI? api;
    private Entity? entity;
    private ApproximateTimePeriod? period;
    private bool enabled = true;
    private bool disposed;

    /// <summary>Controls retained artwork during visibility changes.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;

    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => Clear();
    #region Public API
    #region Lifecycle
    /// <summary>Creates an asset-backed icon presentation with optional headless loading.</summary>
    public ApproximateTimeOverlay(ApproximateTimePresentation? presentation = null)
    {
        this.presentation = presentation ?? new();
    }
    /// <summary>Binds one client world for presentation.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("An approximate time session is already bound.");
        this.api = api;
    }
    /// <summary>Clears presentation when disabled without changing saved observation age.</summary>
    public bool SetEnabled(bool value)
    {
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Releases world and presentation bindings.</summary>
    public void EndSession()
    {
        api = null;
        Clear();
        presentation.Reset();
    }
    /// <summary>Releases borrowed artwork bindings and session bindings exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        presentation.Dispose();
    }
    #endregion
    #region Sampling and presentation
    /// <summary>Requires an enabled indicator and a living local character.</summary>
    public bool IsApplicable()
    {
        bool applicable = !disposed && enabled && api?.World.Player?.Entity?.Alive == true;
        if (!applicable) Clear();
        return applicable;
    }
    /// <summary>Reads synchronized observation age and samples the native calendar outside the drawing path.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var current = api!.World.Player.Entity;
        if (!ReferenceEquals(current, entity))
        {
            Clear();
            entity = current;
        }
        var calendar = api.World.Calendar;
        var next = ApproximateTimePeriod.Unknown;
        double estimatedDay = double.NaN;
        double? observedDayOffset = ApproximateTimeEstimate.GetDayOffset(ApproximateTimeState.Read(current),
            calendar.HoursPerDay, api.World.Player.PlayerUID);
        if (observedDayOffset is double dayOffset)
        {
            // Ask the native calendar for the seasonal solar path at the remembered time, not the true
            // current time. Drift therefore still affects the icon while away from sunlight.
            estimatedDay = calendar.TotalDays + dayOffset;
            var location = current.Pos.XYZ;
            float height = calendar.GetSunPosition(location, estimatedDay).Y;
            next = ApproximateTimeSolarPeriod.FromSunHeight(
                calendar.GetSunPosition(location, estimatedDay - .02).Y,
                height,
                calendar.GetSunPosition(location, estimatedDay + .02).Y, period);
        }
        // A transient invalid calendar sample must not replace a valid icon with an hourglass.
        // Before the first usable sample, leave the presentation empty and retry next refresh.
        if (next == ApproximateTimePeriod.Unknown) return HudOverlayChange.None;
        bool periodChanged = next != period;
        float? temperature = null;
        if (VanillaExpandedModSystem.Config.EnableEnvironmentalTemperatureOverlay)
        {
            var climate = api.World.BlockAccessor.GetClimateAt(current.Pos.AsBlockPos, EnumGetClimateMode.NowValues);
            if (climate != null && float.IsFinite(climate.Temperature)) temperature = climate.Temperature;
        }
        presentation.SetTemperature(temperature);
        period = next;
        presentation.SetContent(next, estimatedDay);
        return HudOverlayChange.Presentation | (periodChanged ? HudOverlayChange.Measurement : HudOverlayChange.None);
    }
    /// <summary>Prepares cached artwork for the estimated part of the day.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (period.HasValue) presentation.Prepare(context);
    }
    /// <summary>Returns prepared icon dimensions in unscaled GUI units.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Draws cached artwork without calendar or terrain queries.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        presentation.Draw(renderer, bounds);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears character-specific presentation and releases borrowed artwork bindings.</summary>
    private void Clear()
    {
        entity = null;
        period = null;

        presentation.Hide();
    }
    #endregion
}
