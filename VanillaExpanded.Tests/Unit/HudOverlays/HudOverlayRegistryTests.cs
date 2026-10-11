using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies registry identity, stable passes and exact feature/session ownership without a game or graphics context.</summary>
public sealed class HudOverlayRegistryTests
{
    #region Public API
    #region Registration and metadata
    /// <summary>Rejected duplicate IDs/instances and unknown groups cannot take ownership or alter membership.</summary>
    [Fact]
    public void RejectionsPreserveCallerOwnershipAndMembership()
    {
        using var registry = CreateRegistry();
        var first = new Probe();
        registry.Register(Registration("mod:first", first));
        var duplicate = new Probe();
        var unknown = new Probe();
        Assert.Throws<ArgumentException>(() => registry.Register(Registration("mod:first", duplicate)));
        Assert.Throws<ArgumentException>(() => registry.Register(Registration("mod:unknown", unknown, group: "missing")));
        Assert.Throws<ArgumentException>(() => registry.Register(Registration("mod:alias", first)));
        Assert.Throws<ArgumentException>(() => registry.RegisterGroup(Group()));
        Assert.Equal(new[] { "mod:first" }, Ids(registry));
        Assert.Equal(0, duplicate.Disposals);
        Assert.Equal(0, unknown.Disposals);
        Assert.Single(registry.GetGroups());
        registry.Dispose();
        Assert.Equal(1, first.Disposals);
        Assert.Equal(0, duplicate.Disposals);
        Assert.Equal(0, unknown.Disposals);
    }

    /// <summary>Multiple live placement changes remain atomic until the next pass without touching overlay ownership.</summary>
    [Fact]
    public void GroupUpdatesApplyLatestMetadataAtNextBoundary()
    {
        using var registry = CreateRegistry(); var probe = new Probe();
        registry.Register(Registration("mod:test", probe)); var original = registry.GetGroups().Single();
        var first = new HudOverlayGroup(original.Id, new HudOverlayPlacement("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle), original.Packing);
        var latest = new HudOverlayGroup(original.Id, new HudOverlayPlacement("screen", HudOverlayPoint.RightBottom, HudOverlayPoint.RightBottom, 3, 4), original.Packing);
        registry.RunPass(_ =>
        {
            registry.UpdateGroup(first); registry.UpdateGroup(latest);
            Assert.Same(original, registry.GetGroups().Single());
        });
        Assert.Same(original, registry.GetGroups().Single());
        registry.RunPass(_ => Assert.Same(latest, registry.GetGroups().Single()));
        Assert.Equal(0, probe.Disposals); Assert.Equal(0, probe.Ends);
        Assert.Throws<ArgumentException>(() => registry.UpdateGroup(new HudOverlayGroup("missing", latest.Placement)));
        Assert.Same(latest, registry.GetGroups().Single());
    }

    /// <summary>Metadata cannot smuggle nonpositive scheduling intervals into the future scheduler.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefreshIntervalsMustBePositive(int interval)
    {
        var overlay = new Probe();
        Assert.Throws<ArgumentOutOfRangeException>(() => new HudOverlayRegistration("mod:test", overlay, () => true, "group", refreshIntervalMs: interval));
        Assert.Equal(0, overlay.Disposals);
    }

    /// <summary>Stable IDs have both namespace and name, without ambiguous whitespace or extra separators.</summary>
    [Theory]
    [InlineData("test")]
    [InlineData(":test")]
    [InlineData("mod:")]
    [InlineData("mod:a:b")]
    [InlineData("mod: a")]
    public void StableIdsMustBeNamespaced(string id)
    {
        Assert.Throws<ArgumentException>(() => Registration(id, new Probe()));
    }

    /// <summary>Registration retains a live selector and the agreed packing/scheduling defaults.</summary>
    [Fact]
    public void DefaultsAndLiveEnableSelectorArePreserved()
    {
        bool enabled = false;
        var registration = new HudOverlayRegistration("mod:test", new Probe(), () => enabled, "group");
        Assert.False(registration.IsEnabled());
        enabled = true;
        Assert.True(registration.IsEnabled());
        Assert.Equal(1000, registration.RefreshIntervalMs);
        var packing = Group().Packing;
        Assert.Equal(6, packing.Gap);
        Assert.Equal(4, packing.Padding);
        Assert.Equal(HudOverlayDirection.Vertical, packing.Direction);
        Assert.Equal(HudOverlayCrossAlignment.Start, packing.CrossAlignment);
    }

    /// <summary>Ordering is numeric then ordinal, independent of insertion timing.</summary>
    [Fact]
    public void MembersUseNumericThenOrdinalOrdering()
    {
        using var registry = CreateRegistry();
        registry.Register(Registration("mod:z", new Probe(), 5));
        registry.Register(Registration("mod:a", new Probe(), 5));
        registry.Register(Registration("mod:A", new Probe(), 5));
        registry.Register(Registration("mod:first", new Probe(), -2));
        Assert.Equal(new[] { "mod:first", "mod:A", "mod:a", "mod:z" }, Ids(registry));
    }

    /// <summary>Group snapshots remain immutable and membership changes become visible at the next boundary.</summary>
    [Fact]
    public void GroupMutationAndSnapshotsRespectPassBoundary()
    {
        using var registry = CreateRegistry();
        registry.Register(Registration("mod:one", new Probe()));
        var before = registry.GetGroups();
        registry.RunPass(_ =>
        {
            registry.RegisterGroup(new HudOverlayGroup("later", Group().Placement));
            Assert.Single(registry.GetGroups());
            Assert.Throws<ArgumentException>(() => registry.RegisterGroup(new HudOverlayGroup("later", Group().Placement)));
        });
        Assert.Single(before);
        Ids(registry);
        Assert.Equal(2, registry.GetGroups().Count);
        Assert.Single(before);
        Assert.Throws<NotSupportedException>(() => ((IList<HudOverlayGroup>)before).Clear());
    }
    #endregion

    #region Pass boundaries and threading
    /// <summary>Removing and registering while visiting does not change the current pass or prematurely free resources.</summary>
    [Fact]
    public void MutationAndRemovalWaitForNextPass()
    {
        using var registry = CreateRegistry();
        var old = new Probe();
        IDisposable handle = registry.Register(Registration("mod:a", old));
        registry.Register(Registration("mod:b", new Probe()));
        var seen = new List<string>();
        registry.RunPass(registration =>
        {
            seen.Add(registration.Id);
            if (registration.Id == "mod:a")
            {
                handle.Dispose();
                handle.Dispose();
                registry.Register(Registration("mod:c", new Probe()));
                Assert.Equal(0, old.Disposals);
                Assert.Throws<ArgumentException>(() => registry.Register(Registration("mod:a", new Probe())));
            }
        });
        Assert.Equal(new[] { "mod:a", "mod:b" }, seen);
        Assert.Equal(new[] { "mod:b", "mod:c" }, Ids(registry));
        Assert.Equal(1, old.Disposals);
    }

    /// <summary>A member registered during an active-session pass binds before its first eligible visit.</summary>
    [Fact]
    public void RegistrationDuringSessionPassBindsBeforeFirstVisit()
    {
        using var registry = CreateRegistry();
        registry.Register(Registration("mod:a", new Probe()));
        registry.BeginSession(Api());
        var added = new Probe();
        registry.RunPass(_ => registry.Register(Registration("mod:b", added)));
        Assert.Equal(0, added.Begins);
        registry.RunPass(registration =>
        {
            if (registration.Id == "mod:b") Assert.Equal(1, added.Begins);
        });
    }

    /// <summary>Exceptions in a visitor release the pass guard and preserve pending work.</summary>
    [Fact]
    public void ThrowingPassKeepsDeferredRemovalAndRejectsNestedPass()
    {
        using var registry = CreateRegistry();
        var overlay = new Probe();
        var handle = registry.Register(Registration("mod:a", overlay));
        Assert.Throws<InvalidOperationException>(() => registry.RunPass(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => registry.RunPass(_ => { }));
            handle.Dispose();
            throw new InvalidOperationException("visitor");
        }));
        Assert.Empty(Ids(registry));
        Assert.Equal(1, overlay.Disposals);
    }

    /// <summary>Accepted callbacks during session setup cannot recursively mutate that lifecycle batch.</summary>
    [Fact]
    public void LifecycleCallbackMutationsWaitForBoundary()
    {
        using var registry = CreateRegistry();
        var added = new Probe();
        var initial = new Probe { OnBegin = () => registry.Register(Registration("mod:b", added)) };
        registry.Register(Registration("mod:a", initial));
        registry.BeginSession(Api());
        Assert.Equal(0, added.Begins);
        Assert.Equal(new[] { "mod:a", "mod:b" }, Ids(registry));
        Assert.Equal(1, added.Begins);
    }

    /// <summary>The creating client thread owns all mutation, traversal, lifetime and removal calls.</summary>
    [Fact]
    public void OtherThreadsCannotMutateIterateOrRemove()
    {
        using var registry = CreateRegistry();
        var overlay = new Probe();
        var handle = registry.Register(Registration("mod:a", overlay));
        Exception? observed = null;
        var failures = new List<Exception>();
        var thread = new Thread(() =>
        {
            try { handle.Dispose(); }
            catch (Exception failure) { observed = failure; }
            foreach (Action action in new Action[] { () => registry.RunPass(_ => { }),
                () => registry.RegisterGroup(new HudOverlayGroup("other", Group().Placement)), () => registry.Dispose() })
            {
                try { action(); }
                catch (Exception failure) { failures.Add(failure); }
            }
        });
        thread.Start();
        thread.Join();
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(3, failures.Count);
        Assert.All(failures, failure => Assert.IsType<InvalidOperationException>(failure));
        Assert.Equal(0, overlay.Disposals);
        handle.Dispose();
        Assert.Equal(1, overlay.Disposals);
    }
    #endregion

    #region Session and final ownership
    /// <summary>World rebinding keeps the same registered instance/handle while releasing and recreating session resources.</summary>
    [Fact]
    public void SessionRebindingRetainsRegistrationAndEndsBeforeFinalDisposal()
    {
        var registry = CreateRegistry();
        var overlay = new Probe();
        var handle = registry.Register(Registration("mod:a", overlay));
        var first = Api();
        var second = Api();
        registry.BeginSession(first);
        Assert.Throws<InvalidOperationException>(() => registry.BeginSession(second));
        registry.EndSession();
        registry.EndSession();
        registry.BeginSession(second);
        Assert.Same(second, overlay.Api);
        Assert.Equal(2, overlay.Begins);
        Assert.Equal(1, overlay.Ends);
        handle.Dispose();
        handle.Dispose();
        registry.Dispose();
        registry.Dispose();
        Assert.Equal(new[] { "begin", "end", "begin", "end", "dispose" }, overlay.Events);
        Assert.Null(overlay.Api);
        Assert.Equal(1, overlay.Disposals);
    }

    /// <summary>Removing an accepted but not yet activated instance prevents later session initialization.</summary>
    [Fact]
    public void RemovalBeforeActivationNeverBeginsSession()
    {
        using var registry = CreateRegistry();
        registry.BeginSession(Api());
        var overlay = new Probe();
        var handle = registry.Register(Registration("mod:a", overlay));
        handle.Dispose();
        Assert.Empty(Ids(registry));
        Assert.Equal(0, overlay.Begins);
        Assert.Equal(1, overlay.Disposals);
        Assert.Throws<ArgumentException>(() => registry.Register(Registration("mod:retired", overlay)));
    }

    /// <summary>A stale removal handle cannot remove a later instance registered under the same stable ID.</summary>
    [Fact]
    public void StaleHandleCannotRemoveReplacement()
    {
        using var registry = CreateRegistry();
        var first = new Probe();
        var oldHandle = registry.Register(Registration("mod:a", first));
        oldHandle.Dispose();
        var replacement = new Probe();
        registry.Register(Registration("mod:a", replacement));
        oldHandle.Dispose();
        Assert.Equal(new[] { "mod:a" }, Ids(registry));
        Assert.Equal(0, replacement.Disposals);
    }

    /// <summary>Registration in an active session returns its ownership handle before deferred consumer initialization.</summary>
    [Fact]
    public void LateRegistrationBindsAtBoundaryAndSkipsEndedSession()
    {
        using var registry = CreateRegistry();
        registry.BeginSession(Api());
        var skipped = new Probe();
        registry.Register(Registration("mod:skip", skipped));
        registry.EndSession();
        Ids(registry);
        Assert.Equal(0, skipped.Begins);
        registry.BeginSession(Api());
        var late = new Probe();
        registry.Register(Registration("mod:late", late));
        Assert.Equal(0, late.Begins);
        Ids(registry);
        Assert.Equal(1, late.Begins);
    }

    /// <summary>Partial session initialization is ended safely, other consumers still bind, and registrations remain owned.</summary>
    [Fact]
    public void PartialInitializationFailureIsCleanedAndCanRebind()
    {
        using var registry = CreateRegistry();
        var bad = new Probe { ThrowBegin = true };
        var good = new Probe();
        registry.Register(Registration("mod:a", bad));
        registry.Register(Registration("mod:b", good));
        Assert.Throws<AggregateException>(() => registry.BeginSession(Api()));
        Assert.Equal(1, bad.Ends);
        Assert.Null(bad.Api);
        Assert.Equal(1, good.Begins);
        Assert.Equal(0, bad.Disposals);
        registry.EndSession();
        bad.ThrowBegin = false;
        registry.BeginSession(Api());
        Assert.Equal(2, bad.Begins);
        Assert.Equal(2, good.Begins);
    }

    /// <summary>Final teardown attempts all instances and disposal even when a feature's session teardown fails.</summary>
    [Fact]
    public void CleanupFailureCannotLeakOtherOwnedInstances()
    {
        var registry = CreateRegistry();
        var bad = new Probe { ThrowEnd = true };
        var good = new Probe();
        registry.Register(Registration("mod:a", bad));
        registry.Register(Registration("mod:b", good));
        registry.BeginSession(Api());
        Assert.Throws<AggregateException>(() => registry.Dispose());
        registry.Dispose();
        Assert.Equal(1, bad.Disposals);
        Assert.Equal(1, good.Disposals);
        Assert.Equal(1, good.Ends);
    }

    /// <summary>A teardown callback may remove an owned peer without enqueueing new work or double disposal.</summary>
    [Fact]
    public void TeardownCallbackRemovingPeerDisposesBothOnce()
    {
        var registry = CreateRegistry();
        IDisposable? peerHandle = null;
        var first = new Probe { OnEnd = () => peerHandle!.Dispose() };
        var peer = new Probe();
        registry.Register(Registration("mod:a", first));
        peerHandle = registry.Register(Registration("mod:b", peer));
        registry.BeginSession(Api());
        registry.Dispose();
        peerHandle.Dispose();
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, peer.Disposals);
        Assert.Equal(1, peer.Ends);
    }

    /// <summary>Final disposal before a pending activation releases ownership without binding the queued consumer.</summary>
    [Fact]
    public void DisposalBeforeActivationDoesNotBindPendingConsumer()
    {
        var registry = CreateRegistry();
        registry.BeginSession(Api());
        var overlay = new Probe();
        registry.Register(Registration("mod:a", overlay));
        registry.Dispose();
        Assert.Equal(0, overlay.Begins);
        Assert.Equal(1, overlay.Disposals);
        Assert.Throws<ObjectDisposedException>(() => registry.RunPass(_ => { }));
    }

    /// <summary>Disposal requested inside traversal preserves current membership until the next boundary.</summary>
    [Fact]
    public void DisposalDuringPassReleasesAtNextBoundaryOnly()
    {
        var registry = CreateRegistry();
        var overlay = new Probe();
        registry.Register(Registration("mod:a", overlay));
        registry.RunPass(_ => registry.Dispose());
        Assert.Equal(0, overlay.Disposals);
        Assert.Throws<ObjectDisposedException>(() => registry.Register(Registration("mod:b", new Probe())));
        registry.RunPass(_ => Assert.Fail("Disposed entries must not be visited."));
        Assert.Equal(1, overlay.Disposals);
        registry.Dispose();
        Assert.Throws<ObjectDisposedException>(() => registry.RunPass(_ => { }));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a registry with one declared target/group, without native target resolution.</summary>
    private static HudOverlayRegistry CreateRegistry()
    {
        var registry = new HudOverlayRegistry();
        registry.RegisterGroup(Group());
        return registry;
    }
    /// <summary>Creates immutable group metadata with the agreed hotbar attachment defaults.</summary>
    private static HudOverlayGroup Group() => new("group", new HudOverlayPlacement("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle, 12, 0));
    /// <summary>Creates metadata around an arbitrary passive test consumer.</summary>
    private static HudOverlayRegistration Registration(string id, Probe overlay, int order = 0, string group = "group") => new(id, overlay, () => true, group, order);
    /// <summary>Collects a stable pass's IDs as observable membership/order evidence.</summary>
    private static string[] Ids(HudOverlayRegistry registry)
    {
        var ids = new List<string>();
        registry.RunPass(registration => ids.Add(registration.Id));
        return ids.ToArray();
    }
    /// <summary>Creates borrowed session services whose disposal must never be invoked by registry ownership.</summary>
    private static ICoreClientAPI Api() => new Mock<ICoreClientAPI>(MockBehavior.Strict).Object;
    #endregion

    /// <summary>Arbitrary passive consumer that records exact session/final ownership and simulates partial failures.</summary>
    private sealed class Probe : IHudOverlay
    {
        public int Begins { get; private set; }
        public int Ends { get; private set; }
        public int Disposals { get; private set; }
        public ICoreClientAPI? Api { get; private set; }
        public bool ThrowBegin { get; set; }
        public bool ThrowEnd { get; set; }
        public Action? OnBegin { get; init; }
        public Action? OnEnd { get; init; }
        public List<string> Events { get; } = new();
        #region Public API
        #region Lifecycle
        /// <summary>Records the borrowed session binding before simulating partial initialization.</summary>
        public void BeginSession(ICoreClientAPI api)
        {
            Assert.Null(Api);
            Api = api;
            Begins++;
            Events.Add("begin");
            OnBegin?.Invoke();
            if (ThrowBegin) throw new InvalidOperationException("begin");
        }
        /// <summary>Releases borrowed references without disposing API services, then optionally fails.</summary>
        public void EndSession()
        {
            Api = null;
            Ends++;
            Events.Add("end");
            OnEnd?.Invoke();
            if (ThrowEnd) throw new InvalidOperationException("end");
        }
        /// <summary>Records final owned-resource disposal and asserts session references were released first.</summary>
        public void Dispose()
        {
            Assert.Null(Api);
            Disposals++;
            Events.Add("dispose");
        }
        #endregion
        #region Sampling
        /// <summary>Provides a cheap applicability result with no sampling.</summary>
        public bool IsApplicable() => true;
        /// <summary>Represents stable arbitrary content without gameplay mutation.</summary>
        public HudOverlayChange Refresh() => HudOverlayChange.None;
        #endregion
        #region Presentation
        /// <summary>No resources are required by this headless presentation.</summary>
        public void Prepare(HudOverlayPreparationContext context) { }
        /// <summary>Measures a bounded non-icon presentation in GUI units.</summary>
        public SizeF Measure() => new(20, 10);
        /// <summary>Consumes only prepared state; no game or graphics context is needed by the probe.</summary>
        public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime) { }
        #endregion
        #endregion
    }
}
