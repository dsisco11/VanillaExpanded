using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;

namespace VanillaExpanded.HudOverlays.Updating;

/// <summary>Coalesces feature sampling and caches prepared measurements independently of native positioning.</summary>
internal sealed class HudOverlayScheduler
{
    public const int HeartbeatMs = 100;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly HudOverlayRegistry registry;
    private readonly Func<long> clock;
    private readonly Action<string, Exception>? diagnostic;
    private readonly Dictionary<string, State> states = new(StringComparer.Ordinal);
    private readonly HashSet<string> invalidated = new(StringComparer.Ordinal);
    private long nextHeartbeat;
    private bool started;

    #region Public API
    #region Construction and notifications
    /// <summary>Uses monotonic milliseconds and optional bounded diagnostic delivery without owning registrations.</summary>
    public HudOverlayScheduler(HudOverlayRegistry registry, Func<long>? clockMs = null, Action<string, Exception>? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
        clock = clockMs ?? (() => Environment.TickCount64);
        this.diagnostic = diagnostic;
    }

    /// <summary>Coalesces notifications until an eligible heartbeat; does not sample in the callback.</summary>
    public void Invalidate(string id)
    {
        CheckThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        invalidated.Add(id);
    }

    #endregion
    #region Scheduling
    /// <summary>Observes cheap visibility transitions without gameplay sampling or resource preparation.</summary>
    public void ObserveVisibility(HudOverlayAnchorContext anchors, bool visible, bool enabled = true)
    {
        CheckThread();
        Dictionary<string, HudOverlayGroup>? groups = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        registry.RunPass(registration =>
        {
            groups ??= registry.GetGroups().ToDictionary(group => group.Id, StringComparer.Ordinal);
            seen.Add(registration.Id);
            if (!states.TryGetValue(registration.Id, out var state) || !ReferenceEquals(state.Registration, registration))
                states[registration.Id] = state = new State(registration);
            bool available;
            if (state.Failed && clock() < state.NextRefresh)
            {
                state.Available = enabled && visible && registry.IsSessionBound(registration)
                    && anchors.Resolve(groups[registration.GroupId].Placement.TargetId).HasValue;
                return;
            }
            try
            {
                available = enabled && visible && registry.IsSessionBound(registration) && registration.IsEnabled()
                    && anchors.Resolve(groups[registration.GroupId].Placement.TargetId).HasValue;
            }
            catch (Exception failure) { Fail(state, failure, clock()); available = false; }
            // A restoration never exposes an old snapshot before the next scheduled refresh.
            if (!available || !state.Available) state.NeedsFresh = true;
            state.Available = available;
        }, ReportBoundaryFailure);
        foreach (string id in states.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            states.Remove(id);
            invalidated.Remove(id);
        }
    }

    /// <summary>Runs bounded heartbeat work; overflow membership is intentionally outside scheduling eligibility.</summary>
    public void Update(HudOverlayAnchorContext anchors, HudOverlayPreparationContext preparation, bool visible, bool enabled = true)
    {
        ObserveVisibility(anchors, visible, enabled);
        long now = clock();
        if (started && now < nextHeartbeat) return;
        started = true;
        nextHeartbeat = now + HeartbeatMs;
        // Stay inside a registry pass throughout feature calls so removals cannot dispose an executing overlay.
        registry.RunPass(registration =>
        {
            if (!states.TryGetValue(registration.Id, out var state) || !ReferenceEquals(state.Registration, registration)) return;
            try
            {
                if (state.Failed && now < state.NextRefresh) return;
                if (!registry.IsSessionBound(registration) || !registration.IsEnabled()) { state.Applicable = false; state.NeedsFresh = true; return; }
                state.Applicable = registration.Overlay.IsApplicable();
                if (!state.Applicable) { state.NeedsFresh = true; return; }
                if (!state.Available || state.Failed && now < state.NextRefresh) return;
                bool contextChanged = state.Context != preparation;
                if (state.NeedsFresh || invalidated.Contains(registration.Id) || now >= state.NextRefresh)
                {
                    // Remove before invoking user code: notifications raised during refresh remain pending.
                    invalidated.Remove(registration.Id);
                    HudOverlayChange change = registration.Overlay.Refresh();
                    state.PresentationDirty |= (change & HudOverlayChange.Presentation) != 0;
                    state.MeasurementDirty |= (change & HudOverlayChange.Measurement) != 0;
                    state.NextRefresh = now + registration.RefreshIntervalMs;
                    state.NeedsFresh = false;
                }
                if (contextChanged || state.PresentationDirty)
                {
                    registration.Overlay.Prepare(preparation);
                    state.Context = preparation;
                    state.PresentationDirty = false;
                    // Context changes affect font metrics; content-only changes may explicitly retain size.
                    state.MeasurementDirty |= contextChanged;
                }
                if (state.MeasurementDirty)
                {
                    SizeF size = registration.Overlay.Measure();
                    if (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width < 0 || size.Height < 0)
                        throw new InvalidOperationException("Overlay measurements must be finite and nonnegative.");
                    state.Size = size;
                    state.MeasurementDirty = false;
                }
                state.Failed = false;
                state.Reported = false;
            }
            catch (Exception failure) { Fail(state, failure, now); }
        }, ReportBoundaryFailure);
    }

    #endregion
    #region Prepared state and cleanup
    /// <summary>Provides all eligible measured members, including members suppressed by overflow.</summary>
    public IReadOnlyList<KeyValuePair<HudOverlayRegistration, SizeF>> GetMembers(string groupId)
    {
        CheckThread();
        return states.Values
            .Where(state => state.Registration.GroupId == groupId && Drawable(state))
            .OrderBy(state => state.Registration.Order).ThenBy(state => state.Registration.Id, StringComparer.Ordinal)
            .Select(state => new KeyValuePair<HudOverlayRegistration, SizeF>(state.Registration, state.Size)).ToArray();
    }

    /// <summary>Rejects stale registration identities and unprepared/restoring feature state.</summary>
    public bool IsDrawable(HudOverlayRegistration registration)
    {
        CheckThread();
        return states.TryGetValue(registration.Id, out var state)
            && ReferenceEquals(state.Registration, registration) && Drawable(state);
    }

    /// <summary>Reports one drained lifecycle-boundary failure without interrupting healthy registrations.</summary>
    public void ReportBoundaryFailure(Exception failure)
    {
        CheckThread();
        try { diagnostic?.Invoke("registry boundary", failure); } catch { }
    }

    /// <summary>Contains rendering failures within one feature and schedules a bounded recovery attempt.</summary>
    public void ReportDrawFailure(HudOverlayRegistration registration, Exception failure)
    {
        CheckThread();
        if (states.TryGetValue(registration.Id, out var state) && ReferenceEquals(state.Registration, registration))
            Fail(state, failure, clock());
    }

    /// <summary>Releases session snapshots and queued invalidations without disposing registry-owned features.</summary>
    public void Reset()
    {
        CheckThread();
        states.Clear();
        invalidated.Clear();
        started = false;
        nextHeartbeat = 0;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Enforces the client thread for scheduling, notifications and cached presentation state.</summary>
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("HUD scheduling requires the creating client main thread.");
    }

    /// <summary>Requires successful sampling, preparation and measurement in the current visible eligibility state.</summary>
    private static bool Drawable(State state) => state.Available && state.Applicable && !state.NeedsFresh
        && !state.Failed && !state.PresentationDirty && !state.MeasurementDirty;

    /// <summary>Hides a failed feature and delays retries, with one diagnostic per uninterrupted failure episode.</summary>
    private void Fail(State state, Exception failure, long now)
    {
        state.Failed = true;
        state.NeedsFresh = true;
        state.PresentationDirty = state.MeasurementDirty = true;
        state.NextRefresh = now + state.Registration.RefreshIntervalMs;
        if (state.Reported) return;
        state.Reported = true;
        // A logger failure must never take down the shared host.
        try { diagnostic?.Invoke(state.Registration.Id, failure); } catch { }
    }
    #endregion

    /// <summary>Stores session-local stable measurements and independent sampling/presentation dirtiness.</summary>
    private sealed class State(HudOverlayRegistration registration)
    {
        public HudOverlayRegistration Registration { get; } = registration;
        public bool Available, Applicable, Failed, Reported;
        public bool NeedsFresh = true, PresentationDirty = true, MeasurementDirty = true;
        public long NextRefresh;
        public SizeF Size;
        public HudOverlayPreparationContext? Context;
    }
}
