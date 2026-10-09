using System.Reflection;
using Moq;
using VanillaExpanded.Network;
using VanillaExpanded.SpawnDecal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.SpawnDecal;

/// <summary>Serializes lifecycle tests that change the global decal configuration.</summary>
[CollectionDefinition("SpawnDecalConfig", DisableParallelization = true)]
public sealed class SpawnDecalConfigCollection;

/// <summary>Exercises real renderer resource ownership and network updates without submitting any GL draws.</summary>
[Collection("SpawnDecalConfig")]
public sealed class SpawnDecalClientSystemTests
{
    #region Public API
    /// <summary>Re-enabling creates a new registered renderer seeded from the retained position.</summary>
    [Fact]
    public void DisableEnableRestoresPositionAndReleasesOldMesh()
    {
        using var fixture = new Fixture(true);
        var source = new Vec3d(12, 34, 56);
        fixture.System.SetSpawnPosition(source);
        source.X = 999;
        var original = fixture.CurrentRenderer;
        fixture.SetEnabled(false);
        fixture.Events.Verify(e => e.UnregisterRenderer(original, SpawnDecalRenderer.RenderStage), Times.Once);
        fixture.Meshes[0].Verify(m => m.Dispose(), Times.Once);
        Assert.Null(fixture.CurrentRenderer);

        fixture.SetEnabled(true);
        Assert.NotSame(original, fixture.CurrentRenderer);
        AssertPosition(fixture.CurrentRenderer, new Vec3d(12, 34, 56));
        Assert.Equal(2, fixture.Meshes.Count);
        fixture.Events.Verify(e => e.RegisterRenderer(It.IsAny<IRenderer>(), SpawnDecalRenderer.RenderStage, "spawndecal"), Times.Exactly(2));
    }

    /// <summary>The newest authoritative position received while hidden replaces the old one.</summary>
    [Fact]
    public void DisabledPacketUpdatesPositionUsedWhenEnabled()
    {
        using var fixture = new Fixture(true);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = true, X = 1, Y = 2, Z = 3 });
        fixture.SetEnabled(false);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = true, X = 21, Y = 22, Z = 23 });
        Assert.Single(fixture.Meshes);
        fixture.SetEnabled(true);
        AssertPosition(fixture.CurrentRenderer, new Vec3d(21, 22, 23));
    }

    /// <summary>A clear received while hidden prevents an obsolete spawn from returning.</summary>
    [Fact]
    public void DisabledClearDoesNotResurrectPosition()
    {
        using var fixture = new Fixture(true);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = true, X = 1, Y = 2, Z = 3 });
        fixture.SetEnabled(false);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = false });
        fixture.SetEnabled(true);
        AssertPosition(fixture.CurrentRenderer, null);
    }

    /// <summary>A client initially hidden still retains an incoming sync packet for later rendering.</summary>
    [Fact]
    public void InitiallyDisabledClientRetainsReceivedPacket()
    {
        using var fixture = new Fixture(false);
        Assert.Empty(fixture.Meshes);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = true, X = 7, Y = 8, Z = 9 });
        fixture.SetEnabled(true);
        AssertPosition(fixture.CurrentRenderer, new Vec3d(7, 8, 9));
    }

    /// <summary>Clearing while visible may fade the old renderer but does not seed a replacement.</summary>
    [Fact]
    public void VisibleClearDoesNotResurrectAfterToggle()
    {
        using var fixture = new Fixture(true);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = true, X = 1, Y = 2, Z = 3 });
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = false });
        fixture.SetEnabled(false);
        fixture.SetEnabled(true);
        AssertPosition(fixture.CurrentRenderer, null);
    }

    /// <summary>Disposing the client discards retained spawn state when the same instance starts again.</summary>
    [Fact]
    public void FullDisposeClearsRetainedPosition()
    {
        using var fixture = new Fixture(true);
        fixture.Receive(new Packet_TemporalSpawn { HasSpawn = true, X = 1, Y = 2, Z = 3 });
        fixture.System.Dispose();
        fixture.System.StartClientSide(fixture.Api.Object);
        AssertPosition(fixture.CurrentRenderer, null);
    }
    #endregion

    #region Private
    /// <summary>Observes the real renderer's position without invoking rendering or adding a production test API.</summary>
    private static void AssertPosition(SpawnDecalRenderer? renderer, Vec3d? expected)
    {
        Assert.NotNull(renderer);
        var actual = (Vec3d?)typeof(SpawnDecalRenderer).GetField("spawnPosition", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);
        if (expected is null) Assert.Null(actual);
        else
        {
            Assert.NotNull(actual);
            Assert.Equal(expected.X, actual.X);
            Assert.Equal(expected.Y, actual.Y);
            Assert.Equal(expected.Z, actual.Z);
        }
    }

    /// <summary>Provides mocked engine services while allowing the production renderer and network callback to run.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly bool originalEnabled = VanillaExpandedModSystem.Config.EnableSpawnDecal;
        private NetworkServerMessageHandler<Packet_TemporalSpawn>? packetHandler;
        public readonly Mock<ICoreClientAPI> Api = new();
        public readonly Mock<IClientEventAPI> Events = new();
        public readonly List<Mock<MeshRef>> Meshes = [];
        public readonly SpawnDecalClientSystem System = new();
        public SpawnDecalRenderer? CurrentRenderer;

        #region Public API
        /// <summary>Starts the client and captures its actual network handler and renderer registration.</summary>
        public Fixture(bool initiallyEnabled)
        {
            VanillaExpandedModSystem.Config.EnableSpawnDecal = initiallyEnabled;
            var render = new Mock<IRenderAPI>();
            render.Setup(r => r.UploadMesh(It.IsAny<MeshData>())).Returns(() =>
            {
                var mesh = new Mock<MeshRef>();
                Meshes.Add(mesh);
                return mesh.Object;
            });
            render.Setup(r => r.GetOrLoadTexture(It.IsAny<AssetLocation>())).Returns(17);
            Events.Setup(e => e.RegisterRenderer(It.IsAny<IRenderer>(), SpawnDecalRenderer.RenderStage, "spawndecal"))
                .Callback<IRenderer, EnumRenderStage, string>((renderer, _, _) => CurrentRenderer = (SpawnDecalRenderer)renderer);
            Events.Setup(e => e.UnregisterRenderer(It.IsAny<IRenderer>(), SpawnDecalRenderer.RenderStage))
                .Callback(() => CurrentRenderer = null);
            Api.SetupGet(a => a.Render).Returns(render.Object);
            Api.SetupGet(a => a.Event).Returns(Events.Object);
            var channel = new Mock<IClientNetworkChannel>();
            channel.Setup(c => c.SetMessageHandler(It.IsAny<NetworkServerMessageHandler<Packet_TemporalSpawn>>()))
                .Callback<NetworkServerMessageHandler<Packet_TemporalSpawn>>(handler => packetHandler = handler)
                .Returns(channel.Object);
            var network = new Mock<IClientNetworkAPI>();
            network.Setup(n => n.GetChannel(Constants.ModId)).Returns(channel.Object);
            Api.SetupGet(a => a.Network).Returns(network.Object);
            // The loader owns these internal setters; supply only its metadata needed by client startup.
            var mod = new Mock<Mod>();
            typeof(Mod).GetProperty(nameof(Mod.Info))!.SetValue(mod.Object, new ModInfo { ModID = Constants.ModId });
            typeof(ModSystem).GetProperty(nameof(ModSystem.Mod))!.SetValue(System, mod.Object);
            System.StartClientSide(Api.Object);
            Assert.NotNull(packetHandler);
        }

        /// <summary>Delivers an authoritative update through the handler registered by client startup.</summary>
        public void Receive(Packet_TemporalSpawn packet) => packetHandler!(packet);

        /// <summary>Applies a live enable setting through the existing configuration reload boundary.</summary>
        public void SetEnabled(bool enabled)
        {
            VanillaExpandedModSystem.Config.EnableSpawnDecal = enabled;
            System.OnConfigReloaded(Api.Object);
        }

        /// <summary>Releases the system and restores the original shared configuration.</summary>
        public void Dispose()
        {
            System.Dispose();
            VanillaExpandedModSystem.Config.EnableSpawnDecal = originalEnabled;
        }
        #endregion
    }
    #endregion
}
