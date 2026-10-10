using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Updating;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Uses a fake monotonic clock to verify bounded work and stable resource preparation.</summary>
public sealed class HudOverlaySchedulerTests
{
    #region Public API
    /// <summary>Multiple invalidations coalesce and refresh intervals receive heartbeat granularity.</summary>
    [Fact]
    public void InvalidationsAndIntervalsAreBounded()
    {
        using var fixture = new Fixture();
        fixture.Tick(0);
        Assert.Equal(1, fixture.Probe.Refreshes);
        fixture.Scheduler.Invalidate("mod:test"); fixture.Scheduler.Invalidate("mod:test");
        fixture.Tick(99); Assert.Equal(1, fixture.Probe.Refreshes);
        fixture.Tick(100); Assert.Equal(2, fixture.Probe.Refreshes);
        fixture.Tick(300); Assert.Equal(2, fixture.Probe.Refreshes);
        fixture.Tick(400); Assert.Equal(3, fixture.Probe.Refreshes);
        Assert.Equal(1, fixture.Probe.Preparations);
        Assert.Equal(1, fixture.Probe.Measurements);
    }

    /// <summary>Disabled, inapplicable, hidden and absent-anchor features do no expensive work.</summary>
    [Theory]
    [InlineData("disabled")]
    [InlineData("inapplicable")]
    [InlineData("hidden")]
    [InlineData("anchor")]
    public void SuspensionsRefreshBeforeRestoring(string suspension)
    {
        using var fixture = new Fixture(); fixture.Tick(0);
        switch (suspension)
        {
            case "disabled": fixture.Enabled = false; break;
            case "inapplicable": fixture.Probe.Applicable = false; break;
            case "hidden": fixture.Visible = false; break;
            case "anchor": fixture.Target = null; break;
        }
        fixture.Scheduler.Invalidate("mod:test"); fixture.Tick(1000);
        Assert.Equal(1, fixture.Probe.Refreshes); Assert.Empty(fixture.Scheduler.GetMembers("group"));
        fixture.Enabled = fixture.Visible = fixture.Probe.Applicable = true;
        fixture.Target = new RectangleF(50, 50, 100, 100);
        fixture.Observe(); Assert.Empty(fixture.Scheduler.GetMembers("group"));
        fixture.Tick(1100);
        Assert.Equal(2, fixture.Probe.Refreshes); Assert.Single(fixture.Scheduler.GetMembers("group"));
        Assert.Equal(1, fixture.Probe.Preparations);
    }

    /// <summary>Visibility transitions between heartbeats cannot expose stale sampled content.</summary>
    [Fact]
    public void TransientAnchorLossRequiresFreshSample()
    {
        using var fixture = new Fixture(); fixture.Tick(0);
        fixture.Target = null; fixture.Observe();
        fixture.Target = new RectangleF(50, 50, 100, 100); fixture.Observe();
        Assert.Empty(fixture.Scheduler.GetMembers("group"));
        fixture.Tick(99); Assert.Empty(fixture.Scheduler.GetMembers("group"));
        fixture.Tick(100); Assert.Equal(2, fixture.Probe.Refreshes);
    }

    /// <summary>Position changes reuse resources while content and preparation context changes rebuild deliberately.</summary>
    [Fact]
    public void ContextAndContentDirtinessRemainSeparate()
    {
        using var fixture = new Fixture(); fixture.Tick(0);
        fixture.Target = new RectangleF(100, 100, 100, 100); fixture.Tick(100);
        Assert.Equal(1, fixture.Probe.Preparations);
        fixture.Probe.Change = HudOverlayChange.Presentation; fixture.Scheduler.Invalidate("mod:test"); fixture.Tick(200);
        Assert.Equal(2, fixture.Probe.Preparations); Assert.Equal(1, fixture.Probe.Measurements);
        fixture.Probe.Change = HudOverlayChange.Measurement; fixture.Scheduler.Invalidate("mod:test"); fixture.Tick(300);
        Assert.Equal(2, fixture.Probe.Preparations); Assert.Equal(2, fixture.Probe.Measurements);
        fixture.Locale = "de"; fixture.Tick(400);
        Assert.Equal(3, fixture.Probe.Preparations); Assert.Equal(3, fixture.Probe.Measurements);
        Assert.Equal(3, fixture.Probe.Refreshes);
    }

    /// <summary>Failures hide only the feature, emit one diagnostic and wait for its interval before retry.</summary>
    [Theory]
    [InlineData("refresh")]
    [InlineData("prepare")]
    [InlineData("measure")]
    public void FailuresHaveBoundedRetryAndDiagnostics(string failure)
    {
        using var fixture = new Fixture(); fixture.Probe.Failure = failure;
        fixture.Tick(0); Assert.Empty(fixture.Scheduler.GetMembers("group")); Assert.Equal(1, fixture.Diagnostics);
        fixture.Scheduler.Invalidate("mod:test"); fixture.Tick(100); Assert.Equal(1, fixture.Probe.Refreshes);
        fixture.Tick(300); Assert.Equal(2, fixture.Probe.Refreshes); Assert.Equal(1, fixture.Diagnostics);
        fixture.Probe.Failure = null; fixture.Tick(600);
        Assert.Single(fixture.Scheduler.GetMembers("group"));
        fixture.Scheduler.ReportDrawFailure(fixture.Registration, new InvalidOperationException());
        Assert.Empty(fixture.Scheduler.GetMembers("group")); Assert.Equal(2, fixture.Diagnostics);
    }

    /// <summary>Notifications raised during refresh survive until the next heartbeat.</summary>
    [Fact]
    public void ReentrantInvalidationIsRetained()
    {
        using var fixture = new Fixture();
        fixture.Probe.OnRefresh = () => fixture.Scheduler.Invalidate("mod:test");
        fixture.Tick(0); fixture.Tick(100); Assert.Equal(2, fixture.Probe.Refreshes);
    }

    /// <summary>Late initialization failure is contained while healthy members continue scheduling.</summary>
    [Fact]
    public void LateBindingFailureDoesNotAbortHealthyMembers()
    {
        using var fixture = new Fixture(); fixture.Tick(0);
        var failed = new Probe { BeginFailure = true };
        fixture.Registry.Register(new HudOverlayRegistration("mod:failed", failed, () => true, "group"));
        fixture.Tick(300);
        Assert.Equal(2, fixture.Probe.Refreshes); Assert.Equal(0, failed.Refreshes);
        Assert.Single(fixture.Scheduler.GetMembers("group")); Assert.Equal(1, fixture.Diagnostics);
    }

    /// <summary>Applicability failures retain their first retry deadline rather than postponing each heartbeat.</summary>
    [Fact]
    public void ApplicabilityFailureRetriesAtItsOriginalDeadline()
    {
        using var fixture = new Fixture(); fixture.Probe.Failure = "applicability"; fixture.Tick(0);
        fixture.Tick(100); fixture.Tick(200);
        Assert.Equal(1, fixture.Probe.ApplicabilityChecks);
        fixture.Probe.Failure = null; fixture.Tick(300);
        Assert.Equal(2, fixture.Probe.ApplicabilityChecks); Assert.Single(fixture.Scheduler.GetMembers("group"));
    }

    /// <summary>A failed session binding never reaches applicability, refresh or preparation.</summary>
    [Fact]
    public void FailedBindingIsNotScheduled()
    {
        using var fixture = new Fixture();
        fixture.Registry.EndSession(); fixture.Probe.BeginFailure = true;
        Assert.Throws<AggregateException>(() => fixture.Registry.BeginSession(fixture.Api));
        fixture.Tick(0); Assert.Empty(fixture.Scheduler.GetMembers("group"));
        Assert.Equal(0, fixture.Probe.Refreshes);
    }

    /// <summary>Overflow omission does not suppress the scheduler refresh interval.</summary>
    [Fact]
    public void OverflowMembersContinueRefreshing()
    {
        using var fixture = new Fixture(); fixture.Tick(0);
        var second = new Probe();
        fixture.Registry.Register(new HudOverlayRegistration("mod:second", second, () => true, "group", order: 1, refreshIntervalMs: 250));
        fixture.Tick(100);
        using var layout = new VanillaExpanded.HudOverlays.Layout.HudOverlayGroupLayout();
        fixture.Target = new RectangleF(50, 50, 100, 100);
        var members = fixture.Scheduler.GetMembers("group");
        var oversized = members.Select(member => new KeyValuePair<HudOverlayRegistration, SizeF>(member.Key, new SizeF(1000, 1000))).ToArray();
        layout.Apply(fixture.Anchors, fixture.Registry.GetGroups()[0], oversized);
        Assert.Single(layout.Members);
        fixture.Tick(400); Assert.Equal(2, second.Refreshes);
        Assert.Equal(2, fixture.Scheduler.GetMembers("group").Count);
    }

    /// <summary>Removal drops cached identities and reset cannot retain prior session samples.</summary>
    [Fact]
    public void RemovalAndSessionResetReleaseCachedState()
    {
        using var fixture = new Fixture(); fixture.Tick(0);
        fixture.Handle.Dispose(); fixture.Observe(); Assert.Empty(fixture.Scheduler.GetMembers("group"));
        Assert.False(fixture.Scheduler.IsDrawable(fixture.Registration));
        fixture.Scheduler.Reset(); Assert.Empty(fixture.Scheduler.GetMembers("group"));
        Assert.Equal(1, fixture.Probe.Disposals);
    }
    #endregion

    #region Private
    /// <summary>Owns fake time and borrowed services for one scheduler session.</summary>
    private sealed class Fixture : IDisposable
    {
        public long Now;
        public bool Enabled = true, Visible = true;
        public RectangleF? Target = new(50, 50, 100, 100);
        public string Locale = "en";
        public int Diagnostics;
        public readonly Probe Probe = new();
        public readonly HudOverlayRegistry Registry = new();
        public readonly HudOverlayScheduler Scheduler;
        public readonly HudOverlayAnchorContext Anchors;
        public readonly ICoreClientAPI Api = Mock.Of<ICoreClientAPI>();
        public readonly HudOverlayRegistration Registration;
        public readonly IDisposable Handle;
        #region Public API
        /// <summary>Creates a bound feature with a 250 ms interval and dynamic named target.</summary>
        public Fixture()
        {
            Registry.RegisterGroup(new HudOverlayGroup("group", new HudOverlayPlacement("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle)));
            Registration = new HudOverlayRegistration("mod:test", Probe, () => Enabled, "group", refreshIntervalMs: 250);
            Handle = Registry.Register(Registration); Registry.BeginSession(Api);
            Scheduler = new HudOverlayScheduler(Registry, () => Now, (_, _) => Diagnostics++);
            Anchors = new HudOverlayAnchorContext(new Window(), () => 1, () => Target);
        }
        /// <summary>Advances scheduling time and shares current native anchor samples.</summary>
        public void Tick(long now)
        {
            Now = now; Anchors.BeginFrame(Visible);
            Scheduler.Update(Anchors, new HudOverlayPreparationContext(Api, 1, Locale), Visible);
        }
        /// <summary>Updates visual availability between heartbeats without sampling.</summary>
        public void Observe() { Anchors.BeginFrame(Visible); Scheduler.ObserveVisibility(Anchors, Visible); }
        /// <summary>Ends registry ownership after the test.</summary>
        public void Dispose() => Registry.Dispose();
        #endregion
    }

    /// <summary>Counts feature work and optionally fails one boundary.</summary>
    private sealed class Probe : IHudOverlay
    {
        public bool Applicable = true, BeginFailure;
        public HudOverlayChange Change;
        public string? Failure;
        public Action? OnRefresh;
        public int Refreshes, Preparations, Measurements, Disposals, ApplicabilityChecks;
        #region Public API
        /// <summary>Accepts the borrowed session without allocating graphics resources.</summary>
        public void BeginSession(ICoreClientAPI api) { if (BeginFailure) throw new InvalidOperationException(); }
        /// <summary>Has no retained borrowed state to release.</summary>
        public void EndSession() { }
        /// <summary>Returns cheap dynamic applicability.</summary>
        public bool IsApplicable() { ApplicabilityChecks++; Throw("applicability"); return Applicable; }
        /// <summary>Counts stable sampling and exercises callback invalidation.</summary>
        public HudOverlayChange Refresh() { Refreshes++; OnRefresh?.Invoke(); Throw("refresh"); return Change; }
        /// <summary>Counts preparation and optional resource failure.</summary>
        public void Prepare(HudOverlayPreparationContext context) { Preparations++; Throw("prepare"); }
        /// <summary>Counts measurement of stable content.</summary>
        public SizeF Measure() { Measurements++; Throw("measure"); return new SizeF(20, 20); }
        /// <summary>Rejects accidental drawing by scheduler work.</summary>
        public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime) => throw new InvalidOperationException();
        /// <summary>Records exact final ownership cleanup.</summary>
        public void Dispose() => Disposals++;
        #endregion
        #region Private
        /// <summary>Injects the selected feature boundary failure.</summary>
        private void Throw(string operation) { if (Failure == operation) throw new InvalidOperationException(operation); }
        #endregion
    }

    /// <summary>Supplies native pixel dimensions without a windowing platform.</summary>
    private sealed class Window : ElementBounds
    {
        #region Public API
        /// <summary>Marks the fake parent as the native window boundary.</summary>
        public Window() { IsWindowBounds = true; }
        /// <summary>Returns the headless window origin without a parent.</summary>
        public override double renderX => 0;
        /// <summary>Returns the headless window origin without a parent.</summary>
        public override double renderY => 0;
        /// <summary>Calculates deterministic viewport bounds.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 800; absInnerHeight = 600; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}
