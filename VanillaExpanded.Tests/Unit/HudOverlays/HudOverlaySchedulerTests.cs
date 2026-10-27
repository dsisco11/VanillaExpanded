using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Updating;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Uses a fake monotonic clock to verify bounded work and stable resource preparation.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlaySchedulerTests : IDisposable
{
    private readonly float oldScale = Vintagestory.API.Config.RuntimeEnv.GUIScale;
    #region Public API
    /// <summary>Exiting content remains drawable and laid out until its fade finishes, then collapses between heartbeats.</summary>
    [Fact]
    public void AnimatedExitRetainsLayoutUntilCompletion()
    {
        using var fixture = new Fixture();
        var overlay = new AnimatedProbe(() => fixture.Now);
        var registration = new HudOverlayRegistration("mod:animated", overlay, () => true, "group");
        fixture.Registry.Register(registration);
        fixture.Tick(0);
        fixture.Tick(300);
        overlay.Applicable = false;
        fixture.Tick(400);
        Assert.True(fixture.Scheduler.IsDrawable(registration));
        Assert.Contains(fixture.Scheduler.GetMembers("group"), member => member.Key == registration);
        fixture.Now = 649;
        Assert.True(fixture.Scheduler.IsDrawable(registration));
        fixture.Now = 650;
        Assert.False(fixture.Scheduler.IsDrawable(registration));
        Assert.DoesNotContain(fixture.Scheduler.GetMembers("group"), member => member.Key == registration);
        overlay.Applicable = true;
        fixture.Tick(700);
        Assert.True(fixture.Scheduler.IsDrawable(registration));
        fixture.Visible = false;
        fixture.Observe();
        Assert.False(fixture.Scheduler.IsDrawable(registration));
    }
    /// <summary>Sets deterministic native scale for headless bounds.</summary>
    public HudOverlaySchedulerTests() => Vintagestory.API.Config.RuntimeEnv.GUIScale = 1;

    /// <summary>Native GUI scaling leaves measured logical bounds unchanged and hidden ordinary members collapsed.</summary>
    [Fact]
    public void NativeGuiScalePreservesMeasuredLayoutAndStillCollapses()
    {
        using var fixture = new Fixture();
        fixture.Anchors.BeginFrame(true);
        var preparation = new HudOverlayPreparationContext(fixture.Api, 2, "en");
        fixture.Scheduler.Update(fixture.Anchors, preparation, true);
        Assert.Equal(2, preparation.GuiScale);
        Assert.Equal(new SizeF(20, 20), Assert.Single(fixture.Scheduler.GetMembers("group")).Value);
        fixture.Enabled = false;
        fixture.Observe();
        Assert.Empty(fixture.Scheduler.GetMembers("group"));
    }
    /// <summary>Restores borrowed native state after each check.</summary>
    public void Dispose() => Vintagestory.API.Config.RuntimeEnv.GUIScale = oldScale;
    /// <summary>Reserved native footprints survive every content and visibility transition, including initial binding failure.</summary>
    [Fact]
    public void ReservedFootprintsRemainStableThroughSuspensionAndRecovery()
    {
        using var fixture = new Fixture(new SizeF(72, 46));
        var second = new Probe();
        var time = new HudOverlayRegistration("mod:time", second, () => true, "group", order: 100, reservedSize: new SizeF(24, 24));
        fixture.Registry.Register(time);
        using var layout = new VanillaExpanded.HudOverlays.Layout.HudOverlayGroupLayout();
        fixture.Probe.Applicable = false;
        fixture.Tick(0);
        layout.Apply(fixture.Anchors, fixture.Registry.GetGroups()[0], fixture.Scheduler.GetMembers("group"));
        var baseline = layout.Members.Select(member => (member.Bounds.renderX, member.Bounds.renderY, member.Bounds.OuterWidth, member.Bounds.OuterHeight, member.Clip)).ToArray();
        Assert.False(fixture.Scheduler.IsDrawable(fixture.Registration));
        var probeClip = layout.Members.Single(member => member.Registration == fixture.Registration).Clip;
        Assert.True(probeClip.Width > 0 && probeClip.Height > 0, probeClip.ToString());
        Assert.Null(VanillaExpanded.HudOverlays.Rendering.HudOverlayHoverLabels.FindTarget(new[] { layout }, fixture.Scheduler.IsDrawable, false, new PointF(probeClip.Left + 1, probeClip.Top + 1)));
        Assert.Equal(0, fixture.Probe.Refreshes);
        foreach (string transition in new[] { "applicable", "empty", "large", "disabled", "failure", "recovery", "binding-failure" })
        {
            fixture.Enabled = transition != "disabled";
            fixture.Probe.Applicable = true;
            fixture.Probe.Size = transition == "empty" ? SizeF.Empty : transition == "large" ? new SizeF(60, 40) : new SizeF(20, 20);
            fixture.Probe.Failure = transition == "failure" ? "refresh" : null;
            fixture.Probe.Change = HudOverlayChange.Measurement;
            if (transition == "binding-failure")
            {
                fixture.Registry.EndSession(); fixture.Probe.BeginFailure = true;
                Assert.Throws<AggregateException>(() => fixture.Registry.BeginSession(fixture.Api));
            }
            fixture.Scheduler.Invalidate("mod:test"); fixture.Tick(fixture.Now + 1000);
            layout.Apply(fixture.Anchors, fixture.Registry.GetGroups()[0], fixture.Scheduler.GetMembers("group"));
            Assert.Equal(baseline, layout.Members.Select(member => (member.Bounds.renderX, member.Bounds.renderY, member.Bounds.OuterWidth, member.Bounds.OuterHeight, member.Clip)).ToArray());
            Assert.Equal(transition is "applicable" or "large" or "recovery", fixture.Scheduler.IsDrawable(fixture.Registration));
            var hovered = VanillaExpanded.HudOverlays.Rendering.HudOverlayHoverLabels.FindTarget(new[] { layout }, fixture.Scheduler.IsDrawable, false, new PointF(probeClip.Left + 1, probeClip.Top + 1));
            Assert.Equal(transition is "applicable" or "large" or "recovery" ? fixture.Registration : null, hovered);
        }
    }
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
        Assert.Equal(3, fixture.Probe.Preparations); Assert.Equal(2, fixture.Probe.Measurements);
        fixture.Locale = "de"; fixture.Tick(400);
        Assert.Equal(4, fixture.Probe.Preparations); Assert.Equal(3, fixture.Probe.Measurements);
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
        public Fixture(SizeF? reservedSize = null)
        {
            Registry.RegisterGroup(new HudOverlayGroup("group", new HudOverlayPlacement("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle)));
            Registration = new HudOverlayRegistration("mod:test", Probe, () => Enabled, "group", refreshIntervalMs: 250, hoverLabelKey: "mod:test-title", reservedSize: reservedSize);
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
    private class Probe : IHudOverlay
    {
        public bool Applicable = true, BeginFailure;
        public HudOverlayChange Change;
        public SizeF Size = new(20, 20);
        public string? Failure;
        public Action? OnRefresh;
        public int Refreshes, Preparations, Measurements, Disposals, ApplicabilityChecks;
        #region Public API
        /// <summary>Accepts the borrowed session without allocating graphics resources.</summary>
        public void BeginSession(ICoreClientAPI api) { if (BeginFailure) throw new InvalidOperationException(); }
        /// <summary>Has no retained borrowed state to release.</summary>
        public void EndSession() { }
        /// <summary>Returns cheap dynamic applicability.</summary>
        public virtual bool IsApplicable() { ApplicabilityChecks++; Throw("applicability"); return Applicable; }
        /// <summary>Counts stable sampling and exercises callback invalidation.</summary>
        public HudOverlayChange Refresh() { Refreshes++; OnRefresh?.Invoke(); Throw("refresh"); return Change; }
        /// <summary>Counts preparation and optional resource failure.</summary>
        public virtual void Prepare(HudOverlayPreparationContext context) { Preparations++; Throw("prepare"); }
        /// <summary>Counts measurement of stable content.</summary>
        public SizeF Measure() { Measurements++; Throw("measure"); return Size; }
        /// <summary>Creates an independent passive native probe when requested by a host.</summary>
        public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => new CallbackView(api);
        /// <summary>Has no visual animation in this lifecycle probe.</summary>
        public void BeginFrame(float deltaTime) { }
        /// <summary>Records exact final ownership cleanup.</summary>
        public void Dispose() => Disposals++;
        #endregion
        #region Private
        /// <summary>Injects the selected feature boundary failure.</summary>
        private void Throw(string operation) { if (Failure == operation) throw new InvalidOperationException(operation); }
        #endregion
    }

    /// <summary>Models an owner retaining its last preparation through an applicability change.</summary>
    private sealed class AnimatedProbe(Func<long> clock) : Probe, IHudOverlayVisibilityTransition
    {
        /// <summary>Uses the fixture clock for rendering as well as sampling.</summary>
        public HudOverlayVisibilityTransition Visibility { get; } = new(clockMs: clock);
        #region Public API
        /// <summary>Starts an exit while the last prepared content remains owned.</summary>
        public void HidePresentation() => Visibility.Hide(() => { });
        /// <summary>Starts an exit when gameplay eligibility is lost.</summary>
        public override bool IsApplicable()
        {
            bool applicable = base.IsApplicable();
            if (!applicable) HidePresentation();
            return applicable;
        }
        /// <summary>Shows freshly prepared content.</summary>
        public override void Prepare(HudOverlayPreparationContext context)
        {
            base.Prepare(context);
            Visibility.Show();
        }
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
