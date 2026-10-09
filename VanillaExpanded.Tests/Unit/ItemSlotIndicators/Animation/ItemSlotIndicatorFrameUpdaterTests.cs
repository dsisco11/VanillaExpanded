using System.Numerics;

using Moq;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.Tests.Mocks;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Animation;

/// <summary>Checks the GUI callback boundary, demand gating, shared reads, provider independence, and callback cleanup.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorFrameUpdaterTests
{
    #region Public API
    /// <summary>One Ortho callback reads the camera once, ignoring item delta time and other stages.</summary>
    [Fact]
    public void OrthoBoundary_UpdatesOnceAndUnsubscribesOnRepeatedDisposal()
    {
        var events = new Mock<IClientEventAPI>();
        var camera = new Mock<IItemSlotIndicatorCameraSource>();
        camera.Setup(c => c.Capture()).Returns(Sample());
        double now = 100;
        using var updater = new ItemSlotIndicatorFrameUpdater(events.Object, camera.Object, () => true, () => now);
        Assert.Equal(0.99, updater.RenderOrder);
        events.Verify(e => e.RegisterRenderer(updater, EnumRenderStage.Ortho, "itemslotindicator-frame"), Times.Once);
        updater.OnRenderFrame(1, EnumRenderStage.Before);
        camera.Verify(c => c.Capture(), Times.Never);
        updater.OnRenderFrame(50, EnumRenderStage.Ortho);
        now += 0.1;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(0.1f, updater.State.Snapshot.TimeSeconds, 5);
        var snapshot = updater.State.Snapshot;
        for (int draw = 0; draw < 250; draw++) Assert.Equal(snapshot, updater.State.Snapshot);
        camera.Verify(c => c.Capture(), Times.Exactly(2));
        updater.Dispose();
        updater.Dispose();
        now += 0.1;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(snapshot, updater.State.Snapshot);
        events.Verify(e => e.UnregisterRenderer(updater, EnumRenderStage.Ortho), Times.Once);
        camera.Verify(c => c.Capture(), Times.Exactly(2));
    }

    /// <summary>Registered motion demand gates all camera reads and resets after clear while animation remains active.</summary>
    [Fact]
    public void RegistrationDemand_GatesCameraAndDoesNotInvalidateProviderSamples()
    {
        var system = new ItemSlotIndicatorSystem { Clock = () => 0 };
        var provider = new CountingProvider();
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(MockItem.CreateNonLightSource(1)) };
        system.Register(provider);
        var events = new Mock<IClientEventAPI>();
        var camera = new Mock<IItemSlotIndicatorCameraSource>();
        var sample = Sample();
        camera.Setup(c => c.Capture()).Returns(() => sample);
        double now = 0;
        using var updater = new ItemSlotIndicatorFrameUpdater(events.Object, camera.Object, () => system.NeedsCameraMotion, () => now);
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        now = 0.1;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(0.1f, updater.State.Snapshot.TimeSeconds, 5);
        camera.Verify(c => c.Capture(), Times.Never);
        system.Register(new CountingProvider(), effect: Effect(false));
        Assert.False(system.NeedsCameraMotion);
        system.Register(new CountingProvider(), effect: Effect(true));
        Assert.True(system.NeedsCameraMotion);
        Assert.True(system.TryGetRenderSelection(slot, out var first));
        now = 0.2;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(Vector2.Zero, updater.State.Snapshot.Motion);
        sample = sample with { Basis = new(new(0.98f, 0, 0.1989975f), Vector3.UnitY, new(-0.1989975f, 0, 0.98f)) };
        now = 0.3;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.True(updater.State.Snapshot.Motion.X > 0);
        for (int draw = 0; draw < 250; draw++)
        {
            Assert.True(system.TryGetRenderSelection(slot, out var selection));
            Assert.Equal(first, selection);
        }
        Assert.Equal(1, provider.Calls);
        camera.Verify(c => c.Capture(), Times.Exactly(2));
        Assert.Throws<ArgumentException>(() => system.Register(provider,
            effect: new("test:motion", "other", "vanillaexpanded_itemslot_frame", needsCameraMotion: true)));
        Assert.True(system.NeedsCameraMotion);
        system.Clear();
        Assert.False(system.NeedsCameraMotion);
        now = 0.4;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(Vector2.Zero, updater.State.Snapshot.Motion);
        camera.Verify(c => c.Capture(), Times.Exactly(2));
        system.Register(provider, effect: Effect(true));
        now = 0.5;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(Vector2.Zero, updater.State.Snapshot.Motion);
    }

    /// <summary>An unavailable camera publishes neutral motion while monotonic GUI animation continues.</summary>
    [Fact]
    public void MissingCamera_DoesNotSuspendAnimation()
    {
        var events = new Mock<IClientEventAPI>();
        var camera = new Mock<IItemSlotIndicatorCameraSource>();
        double now = 0;
        using var updater = new ItemSlotIndicatorFrameUpdater(events.Object, camera.Object, () => true, () => now);
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        now = 0.1;
        updater.OnRenderFrame(0, EnumRenderStage.Ortho);
        Assert.Equal(0.1f, updater.State.Snapshot.TimeSeconds, 5);
        Assert.Equal(Vector2.Zero, updater.State.Snapshot.Motion);
    }

    /// <summary>The cached installed camera-mode accessor can initialize with no graphics context or active world.</summary>
    [Fact]
    public void EngineCameraAdapter_HandlesUnavailableWorld()
    {
        var api = new Mock<ICoreClientAPI>();
        var source = new ItemSlotIndicatorCameraSource(api.Object);
        Assert.Null(source.Capture());
    }
    #endregion

    #region Private
    /// <summary>Creates fixed camera identity and a valid starting basis.</summary>
    private static ItemSlotIndicatorCameraSample Sample() => new(new object(), new object(), new object(),
        EnumCameraMode.FirstPerson, new(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ));

    /// <summary>Creates a distinct time-only or motion-capable registration description.</summary>
    private static ItemSlotIndicatorEffectDefinition Effect(bool motion) => new(motion ? "test:motion" : "test:time",
        "test", "vanillaexpanded_itemslot_frame", needsCameraMotion: motion);

    /// <summary>Records authoritative provider samples independently of animation callbacks.</summary>
    private sealed class CountingProvider : IItemSlotIndicatorProvider
    {
        internal int Calls;
        #region Public API
        /// <summary>Returns a fixed indicator and counts the sample.</summary>
        public bool TryGetIndicator(ItemSlot slot, out ItemSlotIndicator indicator)
        {
            Calls++;
            indicator = new(0.5f, Vector4.One);
            return true;
        }
        #endregion
    }
    #endregion
}
