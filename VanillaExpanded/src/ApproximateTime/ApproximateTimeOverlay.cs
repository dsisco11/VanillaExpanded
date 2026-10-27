using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Reads synchronized observation age and retains prepared approximate-time data.</summary>
internal sealed class ApproximateTimeOverlay : IHudOverlay, IHudOverlayVisibilityTransition
{

    private readonly ApproximateTimePresentation presentation;
    private ICoreClientAPI? api;
    private Entity? entity;
    private ApproximateTimePeriod? period;
    private ApproximateTimeDayPhases? phases;
    private (double Day, int X, int Z, float? Season)? phaseKey;
    private bool enabled = true;
    private bool disposed;

    /// <summary>Controls retained artwork during visibility changes.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;

    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => Clear();
    private bool styleChanged;

    #region Public API
    /// <summary>Retains a style change until the scheduler prepares and measures the new presentation.</summary>
    public void SetStyle(EnvironmentalGaugeStyle style) => styleChanged |= presentation.SetStyle(style);

    #region Lifecycle
    /// <summary>Retains approximate-time presentation data with optional preparation dependencies.</summary>
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
    /// <summary>Clears prepared feature data and session bindings exactly once.</summary>
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
        bool periodChanged = next != period || styleChanged;
        styleChanged = false;
        var position = current.Pos.AsBlockPos;
        var nextKey = (Math.Floor(calendar.TotalDays), position.X, position.Z, calendar.SeasonOverride);
        if (phaseKey != nextKey)
        {
            var sampled = ApproximateTimeDayPhases.Sample(calendar.TotalDays,
                day => calendar.GetSunPosition(current.Pos.XYZ, day).Y);
            // Keep a valid distribution during transient calendar failures and retry next refresh.
            if (sampled != null) { phases = sampled; phaseKey = nextKey; }
        }
        period = next;
        bool readingChanged = presentation.SetContent(next, estimatedDay, phases, calendar.HourOfDay);
        return HudOverlayChange.Presentation | (periodChanged || readingChanged ? HudOverlayChange.Measurement : HudOverlayChange.None);
    }
    /// <summary>Prepares estimated pointer data, localized clock text and canonical dimensions.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (period.HasValue) presentation.Prepare(context);
    }
    /// <summary>Returns prepared icon dimensions in unscaled GUI units.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Creates one independent native composition over the feature's prepared data.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);
    /// <summary>Has no elapsed visual effect; the estimated state advances only through scheduled sampling.</summary>
    public void BeginFrame(float deltaTime) { }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears character-specific sampling and retains the last prepared snapshot during its exit.</summary>
    private void Clear()
    {
        entity = null;
        period = null;
        phases = null;
        phaseKey = null;

        presentation.Hide();
    }
    #endregion
}
