using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Lifecycle;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Server;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies world listener and session resource ownership through a passive host seam.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlaySessionTests
{
    #region Public API
    #region Session ownership
    /// <summary>Listener identity zero remains valid, while registrations survive fresh world bindings.</summary>
    [Fact]
    public void RejoinRetainsRegistrationAndReleasesEachSession()
    {
        using var fixture = new Fixture();
        fixture.Session.EnterWorld();
        Assert.Equal(1, fixture.Probe.Begins);
        fixture.Session.LeaveWorld(); fixture.Session.LeaveWorld();
        fixture.Events.Verify(value => value.UnregisterGameTickListener(0), Times.Once);
        Assert.Equal(1, fixture.Probe.Ends); Assert.Equal(1, fixture.Hosts[0].Disposals);
        fixture.Session.EnterWorld(); Assert.Equal(2, fixture.Probe.Begins);
        Assert.Equal(0, fixture.Probe.Disposals);
        fixture.Session.Dispose(); fixture.Session.Dispose();
        fixture.Registry.Dispose(); Assert.Equal(1, fixture.Probe.Disposals);
        Assert.Equal(2, fixture.Probe.Ends); Assert.All(fixture.Hosts, host => Assert.Equal(1, host.Disposals));
    }

    /// <summary>Entity replacement and temporarily missing players discard stale state before rebinding.</summary>
    [Fact]
    public void PlayerReplacementAndMissingPlayerRebindFreshly()
    {
        using var fixture = new Fixture(); fixture.Session.EnterWorld();
        fixture.Entity = new EntityPlayer(); fixture.Tick();
        Assert.Equal(2, fixture.Probe.Begins); Assert.Equal(1, fixture.Probe.Ends);
        fixture.HasPlayer = false; fixture.Tick(); Assert.Null(fixture.Session.Scheduler);
        fixture.HasPlayer = true; fixture.Tick(); Assert.Equal(3, fixture.Probe.Begins);
    }

    #endregion
    #region Live visibility and placement
    /// <summary>The independent renderer opens and removes hidden edit proxies while gameplay ticks remain paused.</summary>
    [Fact]
    public void RenderObserverReconcilesClosedHostWithoutGameplayTick()
    {
        using var fixture = new Fixture();
        fixture.FeatureEnabled = false; fixture.Grabbed = false;
        fixture.Gui.Object.OpenedGuis.Clear();
        fixture.Session.EnterWorld();
        var host = fixture.Hosts.Single();
        Assert.False(host.IsOpen); Assert.Empty(host.Layouts!["group"].Members);
        int refreshes = fixture.Probe.Refreshes;
        HudOverlayEditFixture.Open(fixture.Api, fixture.Gui);
        fixture.Session.OnRenderFrame(0, EnumRenderStage.Before);
        Assert.True(host.IsOpen); Assert.Single(host.Layouts["group"].Members);
        fixture.Gui.Object.OpenedGuis.Clear();
        fixture.Session.OnRenderFrame(0, EnumRenderStage.Before);
        Assert.False(host.IsOpen); Assert.Empty(host.Layouts["group"].Members);
        Assert.Equal(refreshes, fixture.Probe.Refreshes);
        fixture.Session.LeaveWorld(); fixture.Session.LeaveWorld();
        fixture.Events.Verify(value => value.RegisterRenderer(fixture.Session, EnumRenderStage.Before, "hud-edit-mode"), Times.Once);
        fixture.Events.Verify(value => value.UnregisterRenderer(fixture.Session, EnumRenderStage.Before), Times.Once);
    }

    /// <summary>Editing exposes an item whose configured native anchor is unavailable without saving a replacement position.</summary>
    [Fact]
    public void UnlockedMissingAnchorUsesTransientEditableFallback()
    {
        using var fixture = new Fixture(); fixture.FeatureEnabled = false;
        fixture.Registry.UpdateGroup(new HudOverlayGroup("group", new HudOverlayPlacement("hotbar", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
        fixture.Session.EnterWorld(); var host = fixture.Hosts.Single(); Assert.Empty(host.Layouts!["group"].Members);
        fixture.Grabbed = false; fixture.Tick();
        var layout = host.Layouts["group"]; Assert.True(layout.Available); Assert.Single(layout.Members); Assert.True(host.IsOpen);
        fixture.Gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vintagestory.API.MathTools.Vec2i>()), Times.Never);
        fixture.Grabbed = true; host.BeforeRender!(); Assert.Empty(layout.Members); Assert.False(host.IsOpen);
        fixture.Gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vintagestory.API.MathTools.Vec2i>()), Times.Never);
    }

    /// <summary>Unlocked movement exposes registered hidden content without overriding feature sampling or preparation.</summary>
    [Theory]
    [InlineData("disabled")] [InlineData("inapplicable")] [InlineData("empty")]
    public void UnlockedMouseExposesHiddenMembersWithoutInventingReadings(string cause)
    {
        using var fixture = new Fixture();
        fixture.FeatureEnabled = cause != "disabled";
        fixture.Probe.Applicable = cause != "inapplicable";
        fixture.Probe.Measurement = cause == "empty" ? SizeF.Empty : new SizeF(20, 20);
        fixture.Session.EnterWorld(); var host = fixture.Hosts.Single();
        Assert.Empty(host.Layouts!["group"].Members);
        int refreshes = fixture.Probe.Refreshes, preparations = fixture.Probe.Preparations;
        fixture.Grabbed = false; fixture.Tick();
        Assert.Same(fixture.Registration, Assert.Single(host.Layouts["group"].Members).Registration);
        Assert.False(fixture.Session.Scheduler!.IsDrawable(fixture.Registration));
        Assert.True(host.IsOpen);
        Assert.Equal(refreshes, fixture.Probe.Refreshes); Assert.Equal(preparations, fixture.Probe.Preparations);
        int lettering = fixture.EditPreparations;
        host.BeforeRender!(); host.BeforeRender!();
        Assert.Equal(lettering, fixture.EditPreparations);
        fixture.Grabbed = true; host.BeforeRender!();
        Assert.Empty(host.Layouts["group"].Members); Assert.False(host.IsOpen);
        Assert.Equal(refreshes, fixture.Probe.Refreshes); Assert.Equal(preparations, fixture.Probe.Preparations);
    }

    /// <summary>Global visibility, GUI suppression and missing sessions take precedence over unlocked editing.</summary>
    [Theory]
    [InlineData("global")] [InlineData("guis")] [InlineData("player")]
    public void UnlockedHiddenMembersRespectSessionAndGlobalVisibility(string cause)
    {
        using var fixture = new Fixture(); fixture.FeatureEnabled = false; fixture.Grabbed = false;
        fixture.Session.EnterWorld(); var host = fixture.Hosts.Single(); Assert.Single(host.Layouts!["group"].Members);
        if (cause == "global") fixture.Enabled = false;
        else if (cause == "guis") fixture.Hidden = true;
        else fixture.HasPlayer = false;
        host.BeforeRender!();
        Assert.Empty(host.Layouts["group"].Members); Assert.False(host.IsOpen);
        Assert.Equal(0, fixture.Probe.Refreshes); Assert.Equal(0, fixture.Probe.Preparations);
    }

    /// <summary>Render/configuration paths observe visibility without sampling and wait for a restored heartbeat.</summary>
    [Fact]
    public void VisibilityRestoresOnlyAfterHeartbeat()
    {
        using var fixture = new Fixture(); fixture.Session.EnterWorld();
        Assert.Equal(1, fixture.Probe.Refreshes);
        fixture.Hosts[0].BeforeRender!(); fixture.Session.ConfigurationChanged();
        Assert.Equal(1, fixture.Probe.Refreshes); Assert.Equal(1, fixture.Probe.Preparations);
        fixture.Enabled = false; fixture.Session.ConfigurationChanged();
        Assert.False(fixture.Session.Scheduler!.IsDrawable(fixture.Registration));
        fixture.Enabled = true; fixture.Session.ConfigurationChanged();
        Assert.False(fixture.Session.Scheduler.IsDrawable(fixture.Registration));
        fixture.Tick(); Assert.Equal(2, fixture.Probe.Refreshes);
        fixture.Hidden = true; fixture.Hosts[0].BeforeRender!();
        fixture.Hidden = false; fixture.Session.ConfigurationChanged();
        Assert.False(fixture.Session.Scheduler!.IsDrawable(fixture.Registration));
        fixture.Tick(); Assert.Equal(3, fixture.Probe.Refreshes);
        Assert.True(fixture.Session.Scheduler.IsDrawable(fixture.Registration));
    }

    /// <summary>Feature and global toggles release layout immediately, resume only with fresh samples, and retain the host.</summary>
    [Fact]
    public void RepeatedFeatureAndSystemReloadsReleaseSpaceWithoutRestart()
    {
        using var fixture = new Fixture(); fixture.Session.EnterWorld();
        var host = fixture.Hosts.Single(); var layout = host.Layouts!["group"];
        for (int index = 0; index < 3; index++)
        {
            int refreshed = fixture.Probe.Refreshes, prepared = fixture.Probe.Preparations;
            fixture.FeatureEnabled = false; fixture.Session.ConfigurationChanged();
            Assert.Empty(layout.Members); Assert.False(host.IsOpen);
            fixture.Tick(); Assert.Equal(refreshed, fixture.Probe.Refreshes);
            fixture.FeatureEnabled = true; fixture.Session.ConfigurationChanged();
            Assert.Empty(layout.Members); Assert.Equal(refreshed, fixture.Probe.Refreshes);
            fixture.Tick(); Assert.Single(layout.Members); Assert.True(host.IsOpen);
            Assert.Equal(refreshed + 1, fixture.Probe.Refreshes);
            fixture.Enabled = false; fixture.Session.ConfigurationChanged();
            Assert.Empty(layout.Members); Assert.False(host.IsOpen);
            fixture.Tick(); Assert.Equal(refreshed + 1, fixture.Probe.Refreshes);
            fixture.Enabled = true; fixture.Session.ConfigurationChanged(); Assert.Empty(layout.Members);
            fixture.Tick(); Assert.Single(layout.Members);
            Assert.Equal(refreshed + 2, fixture.Probe.Refreshes);
            Assert.Equal(prepared, fixture.Probe.Preparations);
            Assert.Same(host, fixture.Hosts.Single()); Assert.Equal(1, fixture.Probe.Begins);
        }
    }

    /// <summary>Reserved artwork footprints collapse after exit and reopen through the same effective native group.</summary>
    [Fact]
    public void ReservedMemberFadeCollapseAndReturnUseEffectiveContainerLayout()
    {
        using var fixture = new Fixture(animated: true); fixture.Session.EnterWorld(); var host = fixture.Hosts.Single(); var layout = host.Layouts!["group"];
        fixture.Advance(10000); host.BeforeRender!(); var root = layout.Root; Assert.Single(layout.Members);
        fixture.FeatureEnabled = false; fixture.Session.ConfigurationChanged(); Assert.Single(layout.Members);
        fixture.Advance(10000); host.BeforeRender!(); Assert.Empty(layout.Members); Assert.False(host.IsOpen);
        fixture.FeatureEnabled = true; fixture.Session.ConfigurationChanged(); fixture.Tick(); Assert.Single(layout.Members); Assert.True(host.IsOpen);
        Assert.Same(root, layout.Root); Assert.Equal(1, fixture.Probe.Begins); Assert.Equal(0, fixture.Probe.Disposals);
    }    /// <summary>Placement reloads repack cached measurements without sampling, preparing, or replacing native roots.</summary>
    [Fact]
    public void RepeatedScreenPlacementReloadsReusePresentationAndNativeRoot()
    {
        using var fixture = new Fixture(); fixture.Session.EnterWorld();
        var host = fixture.Hosts.Single(); var layout = host.Layouts!["group"]; var nativeRoot = layout.Root;
        var original = fixture.Registry.GetGroups().Single();
        foreach (var point in Enum.GetValues<HudOverlayPoint>())
        {
            fixture.Registry.UpdateGroup(new HudOverlayGroup("group",
                new HudOverlayPlacement("screen", point, point, -7, 9), original.Packing));
            fixture.Session.ConfigurationChanged(); fixture.Session.ConfigurationChanged();
            Assert.Same(nativeRoot, layout.Root); Assert.Single(layout.Members);
            Assert.Equal(1, fixture.Probe.Refreshes); Assert.Equal(1, fixture.Probe.Preparations);
            Assert.Same(host, fixture.Hosts.Single()); Assert.Same(original.Packing, fixture.Registry.GetGroups().Single().Packing);
        }
        fixture.Registry.UpdateGroup(new HudOverlayGroup("group",
            new HudOverlayPlacement("hotbar", HudOverlayPoint.RightMiddle, HudOverlayPoint.LeftMiddle), original.Packing));
        fixture.Session.ConfigurationChanged(); Assert.Empty(layout.Members); Assert.False(host.IsOpen);
        fixture.Tick(); Assert.Equal(1, fixture.Probe.Refreshes);
        fixture.Registry.UpdateGroup(original); fixture.Session.ConfigurationChanged(); Assert.Empty(layout.Members);
        fixture.Tick(); Assert.Single(layout.Members); Assert.Equal(2, fixture.Probe.Refreshes);
        Assert.Same(nativeRoot, layout.Root); Assert.Same(host, fixture.Hosts.Single());
    }

    #endregion
    /// <summary>A saved screen origin survives an unavailable registered anchor and unrelated placement changes without preparation.</summary>
    [Fact]
    public void SavedPositionOverridesMissingAnchorAndSurvivesConfigurationChanges()
    {
        using var fixture = new Fixture();
        var saved = new PointF(47, 63);
        fixture.Gui.Setup(value => value.GetDialogPosition(HudOverlayPositionKeys.Group("group")))
            .Returns(new Vintagestory.API.MathTools.Vec2i((int)saved.X, (int)saved.Y));
        var original = fixture.Registry.GetGroups().Single();
        fixture.Registry.UpdateGroup(new HudOverlayGroup("group",
            new HudOverlayPlacement("hotbar", HudOverlayPoint.RightBottom, HudOverlayPoint.RightBottom), original.Packing));
        fixture.Session.EnterWorld();
        var host = fixture.Hosts.Single(); var layout = host.Layouts!["group"];
        Assert.Single(layout.Members); Assert.True(host.IsOpen);
        var root = layout.Root;
        Assert.Equal(saved.X, root.renderX); Assert.Equal(saved.Y, root.renderY);
        fixture.Registry.UpdateGroup(original); fixture.Session.ConfigurationChanged();
        host.BeforeRender!();
        Assert.Same(root, layout.Root);
        Assert.Equal(saved.X, root.renderX); Assert.Equal(saved.Y, root.renderY);
        Assert.Equal(1, fixture.Probe.Refreshes); Assert.Equal(1, fixture.Probe.Preparations);
        fixture.Gui.Verify(value => value.GetDialogPosition(HudOverlayPositionKeys.Group("group")), Times.Once);
        fixture.Session.LeaveWorld(); fixture.Session.EnterWorld();
        Assert.Equal(2, fixture.Probe.Begins); Assert.Equal(1, fixture.Probe.Ends);
        Assert.Equal(saved.X, fixture.Hosts.Last().Layouts!["group"].Root.renderX);
        Assert.Equal(1, host.Disposals);
    }
    #region Cleanup failures
    /// <summary>A native close failure cannot prevent owned disposal or feature session cleanup.</summary>
    [Fact]
    public void CloseFailureStillReleasesOwnedResources()
    {
        using var fixture = new Fixture(); fixture.Session.EnterWorld();
        fixture.Hosts[0].FailClose = true; fixture.Session.LeaveWorld();
        Assert.Equal(1, fixture.Hosts[0].Disposals); Assert.Equal(1, fixture.Probe.Ends);
        Assert.Null(fixture.Session.Scheduler);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Provides mutable borrowed session identities and captures a real heartbeat listener.</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly Mock<ICoreClientAPI> Api = new();
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly Mock<IGuiAPI> Gui = new();
        public readonly Mock<IInputAPI> Input = new();
        public readonly HudOverlayRegistry Registry = new();
        public readonly Probe Probe = new();
        public readonly List<Host> Hosts = new();
        public readonly HudOverlaySession Session;
        public readonly HudOverlayRegistration Registration;
        public EntityPlayer Entity = new();
        public bool HasPlayer = true, Hidden, Enabled = true, FeatureEnabled = true;
        public bool Grabbed = true;
        public int EditPreparations;
        private Action<float>? tick;
        private long now;
        private readonly float oldScale = RuntimeEnv.GUIScale;
        private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
        #region Public API
        /// <summary>Creates real scheduling/layout with fake engine/world and native host services.</summary>
        public Fixture(bool animated = false)
        {
            RuntimeEnv.GUIScale = 1;
            var language = new Mock<ITranslationService>();
            language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
            Lang.AvailableLanguages["en"] = language.Object;
            HudLayoutFixture.BindSettings(Api, new Mock<ISettingsClass<string>>(), new Dictionary<string, string>());
            if (animated) Probe = new AnimatedProbe(() => now);
            var world = new Mock<IClientWorldAccessor>(); var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
            player.SetupGet(value => value.Entity).Returns(() => Entity);
            world.SetupGet(value => value.Player).Returns(() => HasPlayer ? player.Object : null!);
            Api.SetupGet(value => value.World).Returns(world.Object);
            Api.SetupGet(value => value.Event).Returns(Events.Object);
            Api.SetupGet(value => value.HideGuis).Returns(() => Hidden);
            Api.SetupGet(value => value.Input).Returns(Input.Object);
            Input.SetupGet(value => value.MouseGrabbed).Returns(() => Grabbed);
            Gui.SetupGet(value => value.WindowBounds).Returns(new Window());
            Gui.SetupGet(value => value.LoadedGuis).Returns(new List<GuiDialog>());
            Api.SetupGet(value => value.Gui).Returns(Gui.Object);
            HudOverlayEditFixture.Open(Api, Gui);
            Events.Setup(value => value.RegisterGameTickListener(It.IsAny<Action<float>>(), 100, 0))
                .Callback<Action<float>, int, int>((callback, _, _) => tick = callback).Returns(0);
            Registry.RegisterGroup(new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
            Registration = new HudOverlayRegistration("mod:test", Probe, () => FeatureEnabled, "group", reservedSize: animated ? new SizeF(33, 27) : null); Registry.Register(Registration);
            Session = new HudOverlaySession(Api.Object, Registry, (_, _) => { var host = new Host(); Hosts.Add(host); return host; }, () => now, () => Enabled,
                () => new WorkshopGaugeText((context, _) => { EditPreparations++; return new LoadedTexture(context.Api) { TextureId = 42, Width = 80, Height = 12, IgnoreUndisposed = true }; }));
        }
        /// <summary>Advances one heartbeat with the currently exposed world/player.</summary>
        public void Tick() { now += 100; tick!(0); }
        /// <summary>Advances presentation time without sampling to observe exit completion.</summary>
        public void Advance(long elapsed) => now += elapsed;
        /// <summary>Ends session work before final registry ownership.</summary>
        public void Dispose() { try { Session.Dispose(); Registry.Dispose(); } finally { RuntimeEnv.GUIScale = oldScale; if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english; } }
        #endregion
    }

    /// <summary>Records native host ownership without requiring a graphics context.</summary>
    private sealed class Host : IHudOverlayHost
    {
        public Action? BeforeRender { get; set; }
        public int Disposals;
        public bool FailClose;
        public IReadOnlyDictionary<string, HudOverlayGroupLayout>? Layouts;
        public bool IsOpen;
        #region Public API
        /// <summary>Accepts borrowed native group layout.</summary>
        public void Synchronize(IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts) { Layouts = layouts; }
        /// <summary>Ensures native opening is passive.</summary>
        public bool TryOpen(bool withFocus) { Assert.False(withFocus); IsOpen = true; return true; }
        /// <summary>Optionally simulates a native close callback failure.</summary>
        public bool TryClose() { if (FailClose) throw new InvalidOperationException(); IsOpen = false; return true; }
        /// <summary>Records final host cleanup.</summary>
        public void Dispose() => Disposals++;
        #endregion
    }

    /// <summary>Records feature lifetime and stable sampling work.</summary>
    private class Probe : IHudOverlay
    {
        public int Begins, Ends, Disposals, Refreshes, Preparations;
        public bool Applicable = true;
        public SizeF Measurement = new(20, 20);
        #region Public API
        #region Lifecycle
        /// <summary>Records fresh session binding.</summary>
        public void BeginSession(ICoreClientAPI api) => Begins++;
        /// <summary>Records session state cleanup.</summary>
        public void EndSession() => Ends++;
        #endregion
        #region Sampling and presentation
        /// <summary>Allows the test presentation in every ready session.</summary>
        public bool IsApplicable() => Applicable;
        /// <summary>Publishes unchanged stable content.</summary>
        public HudOverlayChange Refresh() { Refreshes++; return HudOverlayChange.None; }
        /// <summary>Records resource preparation.</summary>
        public virtual void Prepare(HudOverlayPreparationContext context) => Preparations++;
        /// <summary>Measures stable logical content dimensions.</summary>
        public SizeF Measure() => Measurement;
        /// <summary>Rejects accidental gameplay/render coupling in lifecycle paths.</summary>
        public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime) => throw new InvalidOperationException();
        #endregion
        /// <summary>Records final feature ownership release.</summary>
        public void Dispose() => Disposals++;
        #endregion
    }

    /// <summary>Retains prepared content during a controllable exit transition.</summary>
    private sealed class AnimatedProbe(Func<long> clock) : Probe, IHudOverlayVisibilityTransition
    {
        public HudOverlayVisibilityTransition Visibility { get; } = new(clockMs: clock);
        #region Public API
        /// <summary>Requests the normal presentation exit without releasing its footprint early.</summary>
        public void HidePresentation() => Visibility.Hide(() => { });
        /// <summary>Shows newly eligible sampled content through the shared transition.</summary>
        public override void Prepare(HudOverlayPreparationContext context) { base.Prepare(context); Visibility.Show(); }
        #endregion
    }    /// <summary>Supplies real native window dimensions without platform access.</summary>
    private sealed class Window : ElementBounds
    {
        #region Public API
        /// <summary>Marks the parent as a native window boundary.</summary>
        public Window() { IsWindowBounds = true; }
        /// <summary>Returns a parentless viewport origin.</summary>
        public override double renderX => 0;
        /// <summary>Returns a parentless viewport origin.</summary>
        public override double renderY => 0;
        /// <summary>Calculates deterministic viewport dimensions.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 800; absInnerHeight = 600; Initialized = true; requiresrelculation = false; }
        #endregion
    }
    #endregion
}
