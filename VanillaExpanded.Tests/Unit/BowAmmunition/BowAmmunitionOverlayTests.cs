using Moq;
using VanillaExpanded.BowAmmunition;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.BowAmmunition;

/// <summary>Checks notification ownership and current-client-state sampling without native font generation.</summary>
[Collection("HudOverlayGeometry")]
public sealed class BowAmmunitionOverlayTests : IDisposable
{
    private readonly Vintagestory.API.Config.ITranslationService? english = Vintagestory.API.Config.Lang.AvailableLanguages.GetValueOrDefault("en");
    #region Public API
    #region Translation fixture
    /// <summary>Installs the repository English empty-state translation for cached presentation checks.</summary>
    public BowAmmunitionOverlayTests()
    {
        var service = new Mock<Vintagestory.API.Config.ITranslationService>();
        service.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] arguments) =>
            key == "vanillaexpanded:bow-ammunition-empty" ? "➶  No arrows / 0" : key);
        Vintagestory.API.Config.Lang.AvailableLanguages["en"] = service.Object;
    }
    /// <summary>Restores borrowed global translation state.</summary>
    public void Dispose()
    {
        if (english == null) Vintagestory.API.Config.Lang.AvailableLanguages.Remove("en");
        else Vintagestory.API.Config.Lang.AvailableLanguages["en"] = english;
    }
    #endregion
    #region Sampling and resource lifecycle
    /// <summary>Events invalidate only, while sampling observes synchronized quantities and clears hand/death state.</summary>
    [Fact]
    public void NotificationsDoNotSampleAndHandDeathDiscardSnapshots()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); int invalidations = 0;
        using var overlay = new BowAmmunitionOverlay(() => invalidations++);
        fixture.Personal[0].Itemstack = new ItemStack(fixture.Flint, 12);
        overlay.BeginSession(fixture.Api.Object); Assert.True(overlay.IsApplicable()); overlay.Refresh();
        fixture.Personal[0].Itemstack!.StackSize = 3;
        fixture.Personal.DidModifyItemSlot(fixture.Personal[0]);
        Assert.Equal(1, invalidations); Assert.Equal(12, overlay.Sample!.Quantity);
        overlay.Refresh(); Assert.Equal(3, overlay.Sample!.Quantity);
        fixture.Hand.Itemstack = null;
        Assert.False(overlay.IsApplicable()); Assert.Null(overlay.Sample);
        fixture.Hand.Itemstack = new ItemStack(fixture.Bow); overlay.Refresh();
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable()); Assert.Null(overlay.Sample);
    }

    /// <summary>Inventory replacement and final session cleanup detach every borrowed event.</summary>
    [Fact]
    public void ReconciliationDetachesRemovedInventoriesAndRepeatedSessionCleanup()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); int invalidations = 0;
        using var subscriptions = new BowAmmunitionInventorySubscriptions(fixture.Api.Object, fixture.Player.Object, () => invalidations++);
        subscriptions.Reconcile(); subscriptions.Reconcile();
        fixture.External.DidModifyItemSlot(fixture.External[0]); Assert.Equal(1, invalidations);
        fixture.Ordered.Remove(fixture.External); subscriptions.Reconcile();
        fixture.External.DidModifyItemSlot(fixture.External[0]); Assert.Equal(1, invalidations);
        subscriptions.Dispose(); subscriptions.Dispose();
        fixture.Personal.DidModifyItemSlot(fixture.Personal[0]); Assert.Equal(1, invalidations);
        fixture.Events.VerifyRemove(value => value.AfterActiveSlotChanged -= It.IsAny<Action<ActiveSlotChangeEventArgs>>(), Times.Once);
    }

    /// <summary>Disabling releases snapshots, resources and events; restoration samples current state afresh.</summary>
    [Fact]
    public void DisableAndSessionReplacementReleaseResourcesAndSubscriptions()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); int invalidations = 0;
        using var overlay = new BowAmmunitionOverlay(() => invalidations++, new BowAmmunitionPresentation(measure: (_, _) => new(24, 12)));
        fixture.Personal[0].Itemstack = new ItemStack(fixture.Flint, 12);
        overlay.BeginSession(fixture.Api.Object); overlay.Refresh();
        overlay.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en"));
        Assert.False(overlay.SetEnabled(false)); Assert.Null(overlay.Sample);
        fixture.Personal.DidModifyItemSlot(fixture.Personal[0]); Assert.Equal(0, invalidations);
        Assert.True(overlay.SetEnabled(true)); Assert.Null(overlay.Sample); Assert.Equal(1, invalidations);
        fixture.Personal[0].Itemstack!.StackSize = 8; overlay.Refresh(); Assert.Equal(8, overlay.Sample!.Quantity);
        overlay.EndSession(); overlay.EndSession(); Assert.Null(overlay.Sample);
        fixture.Personal.DidModifyItemSlot(fixture.Personal[0]); Assert.Equal(1, invalidations);
        overlay.BeginSession(fixture.Api.Object); overlay.Refresh(); Assert.Equal(8, overlay.Sample!.Quantity);
        overlay.Dispose(); overlay.Dispose(); Assert.Null(overlay.Sample);
    }
    #endregion
    #region Notifications and identity
    /// <summary>Local opened membership invalidates after mutation; another player's events do not sample or invalidate.</summary>
    [Fact]
    public void OpenCloseNotificationsRespectLocalIdentity()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); int invalidations = 0;
        fixture.External.InvNetworkUtil = new Mock<IInventoryNetworkUtil>().Object;
        fixture.External.AuditLogAccess = false;
        using var subscriptions = new BowAmmunitionInventorySubscriptions(fixture.Api.Object, fixture.Player.Object, () => invalidations++);
        subscriptions.Reconcile();
        var other = new Mock<Vintagestory.Server.ServerPlayer>((Vintagestory.Server.ServerMain)null!, new Vintagestory.Server.ServerWorldPlayerData()).As<IClientPlayer>(); other.SetupGet(value => value.PlayerUID).Returns("other");
        fixture.External.Open(other.Object); fixture.External.Close(other.Object);
        Assert.Equal(0, invalidations);
        fixture.External.Open(fixture.Player.Object);
        Assert.True(fixture.External.HasOpened(fixture.Player.Object)); Assert.Equal(1, invalidations);
        fixture.External.Close(fixture.Player.Object);
        Assert.False(fixture.External.HasOpened(fixture.Player.Object)); Assert.Equal(2, invalidations);
    }
    /// <summary>No-ammunition preparation retains localized data without sampling during native view creation.</summary>
    [Fact]
    public void EmptyStatePreparesSnapshotOnly()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        using var presentation = new BowAmmunitionPresentation(measure: (_, _) => new(24, 12));
        using var overlay = new BowAmmunitionOverlay(() => { }, presentation);
        overlay.BeginSession(fixture.Api.Object); overlay.Refresh();
        Assert.Null(overlay.Sample!.Icon); Assert.Equal(0, overlay.Sample.Quantity);
        overlay.Prepare(new(fixture.Api.Object, 1, "en"));
        Assert.Equal("➶  No arrows / 0", presentation.Reading);
        fixture.Manager.Invocations.Clear(); fixture.World.Invocations.Clear();
        using var view = overlay.CreateView(fixture.Api.Object, NativeViewRole.Live);
        Assert.Empty(fixture.Manager.Invocations); Assert.Empty(fixture.World.Invocations);
        overlay.EndSession(); Assert.True(presentation.Size.IsEmpty);
    }
    /// <summary>Active-slot events invalidate without reading state and unverified hand items hide.</summary>
    [Fact]
    public void ActiveSlotAndUnsupportedHandKeepSamplingSeparate()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture(); int invalidations = 0;
        using var overlay = new BowAmmunitionOverlay(() => invalidations++);
        overlay.BeginSession(fixture.Api.Object); overlay.Refresh();
        fixture.Manager.Invocations.Clear();
        fixture.Events.Raise(value => value.AfterActiveSlotChanged += null, new ActiveSlotChangeEventArgs(0, 1));
        Assert.Equal(1, invalidations); Assert.Empty(fixture.Manager.Invocations);
        fixture.Hand.Itemstack = new ItemStack(new Item { Code = new AssetLocation("game:custombow"), Tool = EnumTool.Bow });
        Assert.False(overlay.IsApplicable()); Assert.Null(overlay.Sample);
        overlay.EndSession();
        fixture.Events.Raise(value => value.AfterActiveSlotChanged += null, new ActiveSlotChangeEventArgs(0, 1));
        Assert.Equal(1, invalidations);
    }
    /// <summary>A replacement local player discards previous content and rebinds only fresh session state.</summary>
    [Fact]
    public void ReplacementPlayerUsesFreshSampleAndDetachesOldInventories()
    {
        var first = new BowAmmunitionSamplerTests.Fixture(); var second = new BowAmmunitionSamplerTests.Fixture();
        int invalidations = 0; using var overlay = new BowAmmunitionOverlay(() => invalidations++);
        first.Personal[0].Itemstack = new ItemStack(first.Flint, 12);
        second.Personal[0].Itemstack = new ItemStack(second.Copper, 30);
        overlay.BeginSession(first.Api.Object); overlay.Refresh();
        first.World.SetupGet(value => value.Player).Returns(second.Player.Object);
        Assert.False(overlay.IsApplicable()); Assert.Null(overlay.Sample);
        overlay.EndSession(); overlay.BeginSession(second.Api.Object); overlay.Refresh();
        Assert.Same(second.Copper, overlay.Sample!.Icon!.Collectible); Assert.Equal(30, overlay.Sample.Quantity);
        first.Personal.DidModifyItemSlot(first.Personal[0]); Assert.Equal(0, invalidations);
        second.Personal.DidModifyItemSlot(second.Personal[0]); Assert.Equal(1, invalidations);
    }
    #endregion
    #endregion
}
