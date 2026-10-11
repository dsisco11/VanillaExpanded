using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Owns accepted overlay lifetimes and main-thread, pass-boundary membership/session transitions.</summary>
internal sealed class HudOverlayRegistry : IDisposable
{
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, HudOverlayGroup> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HudOverlayGroup> activeGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> owned = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<IHudOverlay, object> retiredInstances = new();
    private readonly HashSet<IHudOverlay> ownedInstances = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Entry> active = new(StringComparer.Ordinal);
    private readonly Queue<Action> pending = new();
    private ICoreClientAPI? session;
    private bool sessionRequested;
    private bool inPass;
    private bool disposalRequested;
    private bool disposed;

    #region Public API
    #region Registration
    /// <summary>Registers one group; duplicate IDs are rejected before any state changes.</summary>
    public void RegisterGroup(HudOverlayGroup group)
    {
        CheckAvailable();
        ArgumentNullException.ThrowIfNull(group);
        if (groups.ContainsKey(group.Id)) throw new ArgumentException("A group with this ID is already registered.", nameof(group));
        // Reserve accepted identities immediately so queued registrations cannot collide.
        groups.Add(group.Id, group);
        Mutate(() => activeGroups.Add(group.Id, group));
    }

    /// <summary>Replaces existing group placement/packing at a pass boundary without changing registration ownership.</summary>
    public void UpdateGroup(HudOverlayGroup group)
    {
        CheckAvailable();
        ArgumentNullException.ThrowIfNull(group);
        if (!groups.ContainsKey(group.Id)) throw new ArgumentException("The group is not registered.", nameof(group));
        if (groups[group.Id] == group) return;
        // Reserve the latest definition immediately; readers see it at the next safe mutation boundary.
        groups[group.Id] = group;
        Mutate(() => activeGroups[group.Id] = group);
    }

    /// <summary>Returns a detached read-only group snapshot, independent of later registration.</summary>
    public IReadOnlyList<HudOverlayGroup> GetGroups()
    {
        CheckAvailable();
        return Array.AsReadOnly(activeGroups.Values.OrderBy(group => group.Id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Accepts lifetime ownership and returns an idempotent removal handle; rejects leave ownership with the caller.</summary>
    public IDisposable Register(HudOverlayRegistration registration)
    {
        CheckAvailable();
        ArgumentNullException.ThrowIfNull(registration);
        if (!groups.ContainsKey(registration.GroupId)) throw new ArgumentException("The overlay references an unknown group.", nameof(registration));
        if (owned.ContainsKey(registration.Id)) throw new ArgumentException("An overlay with this ID is already registered.", nameof(registration));
        if (retiredInstances.TryGetValue(registration.Overlay, out _)) throw new ArgumentException("A finally disposed overlay instance cannot be registered again.", nameof(registration));
        if (ownedInstances.Contains(registration.Overlay)) throw new ArgumentException("This overlay instance is already owned.", nameof(registration));
        var entry = new Entry(registration);
        owned.Add(registration.Id, entry);
        ownedInstances.Add(registration.Overlay);
        var handle = new Removal(this, entry);
        // Acceptance transfers ownership before activation. A session-binding failure is an
        // owned consumer failure, not a rejected registration; the handle remains usable.
        if (inPass || session != null) pending.Enqueue(() => Activate(entry));
        else Mutate(() => Activate(entry));
        return handle;
    }
    #endregion

    #region Iteration
    /// <summary>Identifies the exact active registration with a successfully initialized session binding.</summary>
    public bool IsSessionBound(HudOverlayRegistration registration)
    {
        CheckAvailable();
        return active.TryGetValue(registration.Id, out var entry) && ReferenceEquals(entry.Registration, registration) && entry.SessionBound;
    }

    /// <summary>Applies queued changes at the next boundary, then visits a stable ordered membership snapshot.</summary>
    /// <remarks>Nested passes are rejected. Changes from callbacks become visible only at the following boundary.</remarks>
    public void RunPass(Action<HudOverlayRegistration> visit, Action<Exception>? onBoundaryFailure = null)
    {
        ArgumentNullException.ThrowIfNull(visit);
        RunSnapshotPass(snapshot =>
        {
            foreach (var registration in snapshot) visit(registration);
        }, onBoundaryFailure);
    }

    /// <summary>Visits stable membership; an optional failure handler isolates failed boundary bindings before visiting unaffected entries.</summary>
    public void RunSnapshotPass(Action<IReadOnlyList<HudOverlayRegistration>> visit, Action<Exception>? onBoundaryFailure = null)
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(visit);
        if (inPass) throw new InvalidOperationException("Registry passes cannot be nested.");
        inPass = true;
        try
        {
            // Drain only the batch present at entry. Lifecycle callbacks may enqueue more
            // work, which must not leak into this pass or recursively alter iteration.
            var batch = pending.ToArray();
            pending.Clear();
            List<Exception>? failures = null;
            foreach (var action in batch)
            {
                TryAction(action, ref failures);
                if (disposed) break;
            }
            if (failures != null)
            {
                if (onBoundaryFailure == null) ThrowFailures(failures);
                else onBoundaryFailure(new AggregateException(failures));
            }
            if (disposed) return;
            var snapshot = active.Values.OrderBy(entry => entry.Registration.Order)
                .ThenBy(entry => entry.Registration.Id, StringComparer.Ordinal)
                .Select(entry => entry.Registration).ToArray();
            visit(Array.AsReadOnly(snapshot));
        }
        finally { inPass = false; }
    }
    #endregion

    #region Lifetime
    /// <summary>Binds every current/future member to fresh borrowed session services; requires an inactive registry.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        CheckAvailable();
        ArgumentNullException.ThrowIfNull(api);
        if (sessionRequested) throw new InvalidOperationException("End the previous session before beginning another.");
        sessionRequested = true;
        Mutate(() =>
        {
            session = api;
            List<Exception>? failures = null;
            foreach (var entry in active.Values.ToArray()) TryAction(() => Begin(entry, api), ref failures);
            ThrowFailures(failures);
        });
    }

    /// <summary>Idempotently releases session bindings without disposing registered overlay instances.</summary>
    public void EndSession()
    {
        CheckThread();
        if (disposed || !sessionRequested) return;
        sessionRequested = false;
        Mutate(() =>
        {
            session = null;
            List<Exception>? failures = null;
            foreach (var entry in active.Values.ToArray()) TryAction(() => End(entry), ref failures);
            ThrowFailures(failures);
        });
    }

    /// <summary>Ends active/partially initialized sessions and finally disposes every accepted instance once.</summary>
    public void Dispose()
    {
        CheckThread();
        if (disposalRequested) return;
        disposalRequested = true;
        sessionRequested = false;
        Mutate(() =>
        {
            disposed = true;
            session = null;
            var entries = owned.Values.ToArray();
            active.Clear();
            owned.Clear();
            ownedInstances.Clear();
            groups.Clear();
            activeGroups.Clear();
            // Final teardown must also release queued closures retaining old API or
            // consumer references; boundary batches are detached before execution.
            pending.Clear();
            List<Exception>? failures = null;
            foreach (var entry in entries) TryAction(() => Release(entry), ref failures);
            ThrowFailures(failures);
        });
    }
    #endregion
    #endregion

    #region Private
    #region Membership
    /// <summary>Activates accepted metadata and initializes fresh session state if a session is bound.</summary>
    private void Activate(Entry entry)
    {
        if (entry.Released || disposed) return;
        active.Add(entry.Registration.Id, entry);
        if (session != null) Begin(entry, session);
    }

    /// <summary>Removes the exact accepted entry, avoiding stale handles removing a later replacement ID.</summary>
    private void Remove(Entry entry)
    {
        CheckThread();
        if (disposed || entry.RemovalRequested || entry.Released) return;
        entry.RemovalRequested = true;
        Mutate(() =>
        {
            active.Remove(entry.Registration.Id);
            owned.Remove(entry.Registration.Id);
            // Keep the instance reserved through cleanup so callback code cannot
            // transfer the same retiring object into a second ownership lifetime.
            try { Release(entry); }
            finally { ownedInstances.Remove(entry.Registration.Overlay); }
        });
    }

    /// <summary>Queues mutations during traversal and executes immediate mutations under the same reentrancy guard.</summary>
    private void Mutate(Action action)
    {
        if (inPass) { pending.Enqueue(action); return; }
        inPass = true;
        try { action(); }
        finally { inPass = false; }
    }
    #endregion

    #region Session and final ownership
    /// <summary>Tracks partial initialization before invoking the consumer so failures have an owning cleanup path.</summary>
    private static void Begin(Entry entry, ICoreClientAPI api)
    {
        if (entry.SessionBound) throw new InvalidOperationException("Overlay already has a session.");
        entry.SessionBound = true;
        try { entry.Registration.Overlay.BeginSession(api); }
        catch (Exception beginFailure)
        {
            try { End(entry); }
            catch (Exception endFailure) { throw new AggregateException(beginFailure, endFailure); }
            throw;
        }
    }

    /// <summary>Clears binding before cleanup, making reentrant and repeated teardown safe.</summary>
    private static void End(Entry entry)
    {
        if (!entry.SessionBound) return;
        entry.SessionBound = false;
        entry.Registration.Overlay.EndSession();
    }

    /// <summary>Retires ownership first and attempts final disposal even if session teardown fails.</summary>
    private void Release(Entry entry)
    {
        if (entry.Released) return;
        entry.Released = true;
        retiredInstances.Add(entry.Registration.Overlay, new object());
        List<Exception>? failures = null;
        TryAction(() => End(entry), ref failures);
        TryAction(entry.Registration.Overlay.Dispose, ref failures);
        ThrowFailures(failures);
    }
    #endregion

    #region Validation and error collection
    /// <summary>Rejects registration/query operations after disposal has been requested.</summary>
    private void CheckAvailable()
    {
        CheckThread();
        ObjectDisposedException.ThrowIf(disposalRequested, this);
    }

    /// <summary>Enforces the creating client thread even for idempotent removal and lifetime calls.</summary>
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId) throw new InvalidOperationException("HUD registry operations require the creating client main thread.");
    }

    /// <summary>Collects cleanup/binding failures while allowing all other owned consumers to be serviced.</summary>
    private static void TryAction(Action action, ref List<Exception>? failures)
    {
        try { action(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
    }

    /// <summary>Reports lifecycle errors after the entire owning cleanup/binding batch has run.</summary>
    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures != null) throw new AggregateException(failures);
    }
    #endregion
    #endregion

    /// <summary>Private lifetime state kept separate from immutable feature metadata.</summary>
    private sealed class Entry(HudOverlayRegistration registration)
    {
        public HudOverlayRegistration Registration { get; } = registration;
        public bool SessionBound { get; set; }
        public bool RemovalRequested { get; set; }
        public bool Released { get; set; }
    }

    /// <summary>An idempotent handle tied to one accepted entry rather than a reusable stable ID.</summary>
    private sealed class Removal(HudOverlayRegistry registry, Entry entry) : IDisposable
    {
        #region Public API
        /// <summary>Requests removal on the owning thread; repeated calls cannot dispose a replacement registration.</summary>
        public void Dispose() => registry.Remove(entry);
        #endregion
    }
}
