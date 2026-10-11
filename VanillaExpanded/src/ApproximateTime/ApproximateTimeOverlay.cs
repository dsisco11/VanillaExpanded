using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Samples sunlight exposure and owns the cached approximate time icon.</summary>
internal sealed class ApproximateTimeOverlay : IHudOverlay
{
    private readonly ApproximateTimeEstimate estimate = new();
    private readonly ApproximateTimePresentation presentation;
    private ICoreClientAPI? api;
    private Entity? entity;
    private ApproximateTimePeriod? period;
    private bool enabled = true;
    private bool disposed;

    #region Public API
    #region Lifecycle
    /// <summary>Creates an asset-backed icon presentation with optional headless loading.</summary>
    public ApproximateTimeOverlay(ApproximateTimePresentation? presentation = null)
    {
        this.presentation = presentation ?? new();
    }
    /// <summary>Binds one client world without retaining a previous calibration.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("An approximate time session is already bound.");
        this.api = api;
    }
    /// <summary>Immediately clears calibration and textures when disabled.</summary>
    public bool SetEnabled(bool value)
    {
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Releases world bindings and personal time history.</summary>
    public void EndSession()
    {
        api = null;
        Clear();
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
    /// <summary>Samples the native calendar and overhead cover outside the drawing path.</summary>
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
        var pos = current.Pos.AsBlockPos;
        // Rain-map exposure is the game's inexpensive overhead-cover test. At night the clock drifts,
        // even outdoors; torches and other artificial lights cannot calibrate it.
        bool skyVisible = api.World.BlockAccessor.GetRainMapHeightAt(pos) <= pos.Y;
        bool sunVisible = calendar.SunPositionNormalized.Y > 0 && skyVisible;
        estimate.Observe(calendar.TotalHours, calendar.HourOfDay, calendar.HoursPerDay, sunVisible, current.EntityId);
        var next = ApproximateTimePeriod.Unknown;
        double? observedDayOffset = estimate.EstimatedDayOffset ?? (skyVisible ? 0d : null);
        if (observedDayOffset is double dayOffset)
        {
            // Open sky reveals night or twilight even before a daylight calibration. Under cover,
            // an uncalibrated character still has no observation from which to choose an icon.
            // Ask the native calendar for the seasonal solar path at the remembered time, not the true
            // current time. Drift therefore still affects the icon while away from sunlight.
            double estimatedDay = calendar.TotalDays + dayOffset;
            var location = current.Pos.XYZ;
            float height = calendar.GetSunPosition(location, estimatedDay).Y;
            float coordinate = (Math.Clamp(height * 1.4f + .2f, -1, 1) + 1) / 2;
            float solarIntensity = coordinate;
            if (calendar is Vintagestory.Common.GameCalendar nativeCalendar)
            {
                // Reproduce the native solar-only intensity at estimated time using the owning color lookup.
                // Current DayLightStrength includes moonlight and would also bypass personal clock drift.
                Color sunlight = nativeCalendar.getSunlightPixelRel(Math.Clamp(coordinate + calendar.SunsetMod, 0, 1), .01f);
                solarIntensity = (sunlight.R + sunlight.G + sunlight.B) / (3f * 255)
                    + Math.Max(0, (height - .4f) / 7.5f);
            }
            next = ApproximateTimeSolarPeriod.FromSunHeight(
                calendar.GetSunPosition(location, estimatedDay - .02).Y,
                height,
                calendar.GetSunPosition(location, estimatedDay + .02).Y, solarIntensity);
        }
        if (next == period) return HudOverlayChange.None;
        period = next;
        presentation.SetContent(next);
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    /// <summary>Prepares cached artwork for the estimated part of the day.</summary>
    public void Prepare(HudOverlayPreparationContext context) => presentation.Prepare(context);
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
    /// <summary>Resets character-specific calibration and releases borrowed artwork bindings.</summary>
    private void Clear()
    {
        entity = null;
        period = null;
        estimate.Reset();
        presentation.Reset();
    }
    #endregion
}
